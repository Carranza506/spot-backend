# Spot — Backend

Spot's backend: microservices architecture in .NET 10, coordinated through an API Gateway.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) — verify with `dotnet --version`
- [PostgreSQL 18](https://www.postgresql.org/download/) with the `postgis`, `btree_gist`, and `pgcrypto` extensions enabled
- (Optional but recommended) [Docker Desktop](https://www.docker.com/products/docker-desktop/), to spin up everything with a single command

## Clone the repo

```bash
git clone https://github.com/Carranza506/spot-backend.git
cd spot-backend
```

## Restore dependencies and build

```bash
dotnet restore
dotnet build
```

If this builds without errors, your setup is good.

## Project structure

```
src/
  Spot.Gateway/            → API Gateway, single entry point
  Spot.Auth.Api/           → Authentication and user management
  Spot.Business.Api/       → Businesses, services, and schedules
  Spot.Booking.Api/        → Availability and bookings
  Spot.AiSearch.Api/       → Intelligent search (AI integration)
  Spot.Notifications.Api/  → Push notifications
  Spot.Shared/             → Shared library (DTOs, enums, contracts between services)
```

## Database

All five services share **one physical Postgres database** (`spot_dev`), and **EF Core migrations
are the single authoritative source of the schema** — not a raw SQL script. `db/Spot.sql` is kept
only as a generated, human-readable reference snapshot of the schema (see the note at the end of
this section); it is never applied directly.

Each service owns its own migrations and its own `__EFMigrationsHistory_<service>` table (see each
`Program.cs`), so applying them is safe to do independently per service — but **the order below
must be followed on a fresh database**: `Spot.Auth.Api` creates `users` first, which every other
service's migrations add a foreign key to.

```bash
psql -U postgres -c "CREATE DATABASE spot_dev;"

dotnet ef database update --project src/Spot.Auth.Api          # 1. creates users — must run first
dotnet ef database update --project src/Spot.Business.Api      # 2. requires postgis (see Prerequisites)
dotnet ef database update --project src/Spot.Booking.Api       # 3. requires btree_gist
dotnet ef database update --project src/Spot.AiSearch.Api      # 4.
dotnet ef database update --project src/Spot.Notifications.Api # 5.
```

Each service that needs data access uses its own connection string, configured in `appsettings.Development.json` **(not committed, see the environment variables section below)**.

### Spot.Auth.Api — connection string and migrations

`Spot.Auth.Api` owns `users` and `refresh_tokens` via EF Core (`AuthDbContext`). Set the local
connection string once via user-secrets (never in an `appsettings*.json` file, since it can carry
a password):

```bash
cd src/Spot.Auth.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=spot_dev;Username=postgres;Password=..."
```

In production, set the connection string the same way the JWT keys are set — an environment
variable ASP.NET Core maps automatically: `ConnectionStrings__DefaultConnection`.

> **If you already have a local `spot_dev` from before this change:** each `DbContext` now points
> at a per-service migrations history table (`__EFMigrationsHistory_auth`, `_business`, `_booking`,
> `_aisearch`, `_notifications`) instead of the single shared default `__EFMigrationsHistory`. If
> you had ever actually run `dotnet ef database update` against it, rename your existing
> `__EFMigrationsHistory` row set per service (or just drop `spot_dev` and recreate it with the
> steps above) — otherwise EF will think none of that service's migrations have been applied yet.
>
> **`db/Spot.sql` is a generated reference snapshot, not a setup script.** After applying migrations
> as above, regenerate it with:
> ```bash
> pg_dump -U postgres -d spot_dev --schema-only > db/Spot.sql
> ```
> Do not hand-edit it, and do not `psql -f` it to set up a database — always use the `dotnet ef
> database update` sequence above.


## Environment variables / local configuration

Every project that needs one ships an `appsettings.Example.json` as a template. Before running a service:

```bash
cp src/Spot.Auth.Api/appsettings.Example.json src/Spot.Auth.Api/appsettings.Development.json
```

Then fill in the real values there (connection string, JWT secret, API keys). **Never commit `appsettings.Development.json` or real keys** — it's already in `.gitignore`.

## RSA keys for JWT

Access tokens are RS256 JWTs: `Spot.Auth.Api` signs with an RSA **private** key, and every
microservice — including `Spot.Auth.Api` itself, for its own `[Authorize]` endpoints like
`POST /auth/logout` — validates the signature with the matching **public** key only (via
`AddSpotJwtAuthentication` in `Spot.Shared`). The private key must never be committed or shared
outside `Spot.Auth.Api`.

**1. Generate a local key pair** (from the repo root; requires OpenSSL, bundled with Git for
Windows):

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out jwt-private.pem
openssl rsa -pubout -in jwt-private.pem -out jwt-public.pem
```

`*.pem` is already `.gitignore`d, but keep these outside the repo (e.g. in a personal `keys/`
folder — also ignored) if you want to be extra safe.

**2. Development — load both keys into user-secrets** (never into an `appsettings*.json` file):

```bash
cd src/Spot.Auth.Api
dotnet user-secrets set "Jwt:PrivateKeyPem" "$(cat ../../jwt-private.pem)"
dotnet user-secrets set "Jwt:PublicKeyPem" "$(cat ../../jwt-public.pem)"
```

`Jwt:Issuer`, `Jwt:Audience`, and `Jwt:ExpiresInMinutes` already have non-secret defaults in
`appsettings.Development.json`; only the keys need to be supplied this way.

`Spot.Business.Api` also validates tokens now (#55 — `SUPERADMIN`-gated category management), the
same way: its own user-secrets only need `Jwt:PublicKeyPem` (plus `Jwt:Issuer`/`Jwt:Audience`) — it
must never have access to the private key. The same applies once a future issue wires
`AddSpotJwtAuthentication` into any of the remaining services (Booking, etc.).

**3. Production — environment variables.** ASP.NET Core maps double-underscore env vars to
config sections, so set (on `Spot.Auth.Api`, and on every validating service for the public key):

```bash
Jwt__PrivateKeyPem="$(cat jwt-private.pem)"   # Spot.Auth.Api only
Jwt__PublicKeyPem="$(cat jwt-public.pem)"     # every microservice
Jwt__Issuer="https://api.spot.cr"
Jwt__Audience="spot-clients"
Jwt__ExpiresInMinutes="60"
```

No vault decision has been made yet for production key storage — this is intentionally the only
place that needs to change once one is: `JwtOptions` and the code that consumes it only know about
`IConfiguration`, never about where a value physically comes from.

## Gemini API key

`Spot.AiSearch.Api` calls the Gemini API to parse free-text search queries into structured
criteria. `Gemini:Model`, `Gemini:BaseUrl`, and `Gemini:TimeoutSeconds` already have non-secret
defaults in `appsettings.json`; only the key needs to be supplied, the same way as the JWT keys
above — never in an `appsettings*.json` file:

```bash
cd src/Spot.AiSearch.Api
dotnet user-secrets set "Gemini:ApiKey" "<your-gemini-api-key>"
```

In production, set the `Gemini__ApiKey` environment variable instead.

## Running a single microservice

```bash
dotnet run --project src/Spot.Auth.Api
```

Each service prints its port to the console on startup (check `launchSettings.json` inside each project if unsure which port it lands on).

## Running all services together

Open one terminal per service and run `dotnet run --project src/<Service>` in each — or use
Docker Compose to run everything at once (Postgres, migrations, the 5 data-backed services, and
the Gateway) without needing .NET or `dotnet ef` installed on the host at all. See "Running with
Docker" below.

## Running with Docker

Runs Postgres (with PostGIS), applies all 5 services' EF Core migrations, and starts
`Spot.Auth.Api`, `Spot.Business.Api`, `Spot.Booking.Api`, `Spot.AiSearch.Api`,
`Spot.Notifications.Api`, and `Spot.Gateway` — all inside containers. This is the recommended path
on machines where Windows Application Control / Smart App Control blocks `dotnet ef` when run
directly on the host: every `dotnet`/`dotnet-ef` invocation here happens inside the SDK image,
never on the host.

**`Spot.Gateway` is the single entry point client apps should use** — it reverse-proxies
(via YARP) to the 5 services under `/api/v1/<service>/...` (matching `contracts/spot-api.yaml`'s
path convention), so a client only ever needs to know the Gateway's address. The individual
service ports below are still published for direct debugging (Postman, curl, etc.) but production
and frontend traffic should go through the Gateway.

**Prerequisites:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) running, and
`jwt-private.pem` / `jwt-public.pem` present at the repo root (see "RSA keys for JWT" above —
these are read from disk at `docker compose up` time via Compose secrets, never baked into an
image).

```bash
# Build images and start everything (Postgres -> migrations -> the 5 services -> Gateway, in order)
docker compose up --build

# Subsequent starts (no code changes) can skip --build
docker compose up

# Stop containers, keep data
docker compose down

# Full reset, including the Postgres volume (next `up` re-runs migrations from scratch)
docker compose down -v

# If a service won't start, check whether migrations actually succeeded first
docker compose logs migrator
```

Once running, each service is reachable on its usual host port (unchanged from
`launchSettings.json`, so existing Postman collections / frontend configs keep working), and the
**Gateway is the single entry point** for everything else calling the API:

| Service | URL |
| --- | --- |
| **Spot.Gateway (entry point)** | **http://localhost:5195** |
| Spot.Auth.Api | http://localhost:5228 |
| Spot.Business.Api | http://localhost:5151 |
| Spot.Booking.Api | http://localhost:5197 |
| Spot.AiSearch.Api | http://localhost:5050 |
| Spot.Notifications.Api | http://localhost:5102 |

Through the Gateway, each service is reachable at `http://localhost:5195/api/v1/<segment>/...`
(`auth`, `business`, `booking`, `ai-search`, `notifications` — matching
`contracts/spot-api.yaml`), and each service's `/health` is also proxied individually at
`http://localhost:5195/health/<segment>` (e.g. `/health/auth`) for debugging the proxy itself.

Postgres itself is also published on `localhost:5432` (user/password `postgres`, database
`spot_dev`) for connecting with `psql`/pgAdmin/etc. directly.

Containers run over plain HTTP internally (no dev HTTPS certs inside the containers); the ports
above are HTTP.

Each of the 5 services and the Gateway exposes a liveness probe at `/health` (used by Compose's
own `healthcheck:`), runs as the container's built-in non-root user, and logs structured JSON to
stdout — check status with `docker compose ps` (look for `healthy`) or `docker compose logs
<service>`.

## Photo storage (Spot.Business.Api)

Business and service photos (#65) are stored as files, and only their storage key goes into
`business_photos` / `service_photos`. Storage sits behind `IPhotoStorage`, so it can move to an
object store later without touching the rows.

| Where | Files go to | Notes |
| --- | --- | --- |
| `dotnet run` | `src/Spot.Business.Api/App_Data/photos` | Git-ignored and Docker-ignored |
| Docker Compose | Named volume `business_photos` at `/var/lib/spot/photos` | Survives `down`; **`down -v` deletes it** |

`Photo.url` is built from `PhotoStorage:PublicBaseUrl` (the Gateway's
`/api/v1/business/media/photos`) plus the file name, and is served by the public
`GET /business/media/photos/{fileName}`. Outside Development that URL must be HTTPS, or the
service refuses to start.

What an upload goes through, in order (see `SkiaPhotoImageProcessor`):

1. Size limit while reading the request (5 MB, `413` above it).
2. Format detected from the file's bytes: only JPEG, PNG and WebP. The file name and the
   `Content-Type` sent by the client are ignored.
3. Width × height checked from the header **before decoding** (40 megapixels max).
4. Decode, apply the EXIF orientation, scale down to 2560 px on the longest side.
5. Re-encode in the same format, with no metadata (EXIF/GPS, ICC, XMP are dropped). Only the
   re-encoded image is ever stored.

Files are served with `X-Content-Type-Options: nosniff` and
`Content-Security-Policy: default-src 'none'; sandbox`. File names are random 128-bit ids
generated by the server, so they can't be guessed or used for path traversal.

All limits are configuration, validated at startup:

| Setting | Default |
| --- | --- |
| `PhotoProcessing:MaxBytes` | 5 MB (the HTTP limit in `PhotoUploadForm` matches it) |
| `PhotoProcessing:MaxPixels` | 40,000,000 |
| `PhotoProcessing:MaxDimension` | 2560 |
| `PhotoProcessing:Quality` (JPEG/WebP) | 85 |
| `PhotoProcessing:MaxConcurrentProcessing` | 1 (a decoded 40 MP image takes ~160 MB) |
| `PhotoLimits:MaxPerBusiness` / `MaxPerService` | 20 / 10 |

If a photo's row is deleted but its file can't be, the service logs an `Error` with the storage
key: that file is still reachable by URL and has to be removed by hand.

## Payments (Stripe test mode)

Businesses can subscribe to the PRO plan (₡10,000/month) through Stripe Checkout
(`/api/v1/business/billing/...`, see `contracts/spot-api.yaml`). **Test mode only**: no real money,
and `Spot.Business.Api` refuses any key that isn't `sk_test_...`. Card data never reaches Spot.
Stripe hosts the payment page, and a subscription is only activated by a verified Stripe webhook,
never by the redirect back to the frontend.

### With Docker (recommended)

1. Copy `.env.example` to `.env` at the repo root and ask the team for the values
   (`STRIPE_SECRET_KEY`, `STRIPE_PRO_PRICE_ID`). `.env` is git-ignored: never commit it, and never
   paste the key into `appsettings*.json`, `docker-compose.yml` or any other tracked file.
2. `docker compose up --build`.

The `stripe-cli` service logs in with the API key (no `stripe login` needed). On every start it
writes the webhook signing secret to a volume shared with `business-api`, then forwards Stripe's
events to `http://gateway:8080/api/v1/business/billing/webhooks/stripe`. Check it with
`docker compose logs -f stripe-cli`. Without a `.env` the rest of the stack still runs; only the
billing calls that need Stripe fail, and `stripe-cli` keeps restarting.

Checkout returns to `http://localhost:5173/billing/success` / `/billing/cancel` (the Vite dev
server). To change that, set `STRIPE_SUCCESS_URL` / `STRIPE_CANCEL_URL` in `.env`.

**Paying:** call `POST /api/v1/business/billing/checkout` with a BUSINESS token and
`{ "planCode": "PRO" }`, open the returned `checkoutUrl`, and pay with the test card
`4242 4242 4242 4242`, any future expiry date, any CVC and any postal code. A few seconds later
`GET /api/v1/business/billing/subscription` shows `PRO` / `ACTIVE`, and
`GET /api/v1/business/billing/payments` shows the payment. Other test cards (declines, 3D Secure)
are listed at https://docs.stripe.com/testing.

**Triggering test events:**

```bash
docker compose exec stripe-cli stripe trigger invoice.paid
docker compose exec stripe-cli stripe trigger customer.subscription.deleted
```

`stripe trigger` creates its own fixture customer and subscription, which don't belong to any Spot
account. The webhook verifies them and answers 200, but stores nothing (`business-api` logs
"has no Spot account; ignored"). This checks the forwarding and signature path. To exercise the
full flow, go through a real checkout as above. You can then act on that subscription from the
Stripe dashboard (test mode), e.g. cancel it to receive `customer.subscription.deleted`.

### Without Docker

```bash
# 1. Stripe settings for Business.Api (user-secrets, never appsettings*.json)
cd src/Spot.Business.Api
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
dotnet user-secrets set "Stripe:ProPriceId" "price_..."

# 2. Write the webhook signing secret where Business.Api reads it
#    (Stripe:WebhookSecretFile, App_Data/stripe/webhook-secret by default; App_Data is git-ignored)
mkdir -p App_Data/stripe
stripe listen --api-key sk_test_... --print-secret > App_Data/stripe/webhook-secret
cd ../..

# 3. Run Business.Api and the Gateway (one terminal each)
dotnet run --project src/Spot.Business.Api
dotnet run --project src/Spot.Gateway

# 4. Forward events to the Gateway (another terminal; needs the Stripe CLI installed)
stripe listen --api-key sk_test_... \
  --events checkout.session.completed,invoice.paid,invoice.payment_failed,customer.subscription.updated,customer.subscription.deleted \
  --forward-to http://localhost:5195/api/v1/business/billing/webhooks/stripe
```

`business-api` reads the secret file on every webhook, so rerunning step 2 never needs a restart.

| Setting (`Stripe:*`) | Docker source | Default |
|---|---|---|
| `SecretKey` | `STRIPE_SECRET_KEY` in `.env` | — (required) |
| `ProPriceId` | `STRIPE_PRO_PRICE_ID` in `.env` | — (required; used unless `plans.stripe_price_id` is set) |
| `WebhookSecretFile` | `/run/stripe/webhook-secret` (written by `stripe-cli`) | `App_Data/stripe/webhook-secret` |
| `SuccessUrl` / `CancelUrl` | `STRIPE_SUCCESS_URL` / `STRIPE_CANCEL_URL` (optional) | `http://localhost:5173/billing/success?session_id={CHECKOUT_SESSION_ID}` / `.../billing/cancel` |

`GET /billing/plans` also lists what each plan includes (`features`). Those limits are
informational for now: nothing enforces them yet.

## Team conventions

- Branches: `feature/short-name`, `fix/short-name`
- Commits: short, descriptive messages in English (keep it consistent across the team)
- Pull requests: at least 1 reviewer before merging to `main`
- Changes to `Spot.Shared` (shared DTOs/enums) should be flagged to the team before merging, since they affect multiple microservices at once