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

Access tokens are RS256 JWTs: `Spot.Auth.Api` signs with an RSA **private** key, and every other
microservice validates the signature with the matching **public** key only (via
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

Once a future issue wires `AddSpotJwtAuthentication` into another microservice (Business, Booking,
etc.), that service's own user-secrets only need `Jwt:PublicKeyPem` (plus `Jwt:Issuer`/`Jwt:Audience`)
— it must never have access to the private key.

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

## Running a single microservice

```bash
dotnet run --project src/Spot.Auth.Api
```

Each service prints its port to the console on startup (check `launchSettings.json` inside each project if unsure which port it lands on).

## Running all services together

Open one terminal per service and run `dotnet run --project src/<Service>` in each — or use
Docker Compose to run everything at once (Postgres, migrations, and the 5 data-backed services)
without needing .NET or `dotnet ef` installed on the host at all. See "Running with Docker" below.

## Running with Docker

Runs Postgres (with PostGIS), applies all 5 services' EF Core migrations, and starts
`Spot.Auth.Api`, `Spot.Business.Api`, `Spot.Booking.Api`, `Spot.AiSearch.Api`, and
`Spot.Notifications.Api` — all inside containers. This is the recommended path on machines where
Windows Application Control / Smart App Control blocks `dotnet ef` when run directly on the host:
every `dotnet`/`dotnet-ef` invocation here happens inside the SDK image, never on the host.

`Spot.Gateway` is not containerized yet (not part of this setup); run it separately with
`dotnet run --project src/Spot.Gateway` if you need it.

**Prerequisites:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) running, and
`jwt-private.pem` / `jwt-public.pem` present at the repo root (see "RSA keys for JWT" above —
these are read from disk at `docker compose up` time via Compose secrets, never baked into an
image).

```bash
# Build images and start everything (Postgres -> migrations -> the 5 services, in order)
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
`launchSettings.json`, so existing Postman collections / frontend configs keep working):

| Service | URL |
| --- | --- |
| Spot.Auth.Api | http://localhost:5228 |
| Spot.Business.Api | http://localhost:5151 |
| Spot.Booking.Api | http://localhost:5197 |
| Spot.AiSearch.Api | http://localhost:5050 |
| Spot.Notifications.Api | http://localhost:5102 |

Postgres itself is also published on `localhost:5432` (user/password `postgres`, database
`spot_dev`) for connecting with `psql`/pgAdmin/etc. directly.

Containers run over plain HTTP internally (no dev HTTPS certs inside the containers); the ports
above are HTTP.

Each of the 5 services exposes a liveness probe at `/health` (used by Compose's own
`healthcheck:`), runs as the container's built-in non-root user, and logs structured JSON to
stdout — check status with `docker compose ps` (look for `healthy`) or `docker compose logs
<service>`.

## Team conventions

- Branches: `feature/short-name`, `fix/short-name`
- Commits: short, descriptive messages in English (keep it consistent across the team)
- Pull requests: at least 1 reviewer before merging to `main`
- Changes to `Spot.Shared` (shared DTOs/enums) should be flagged to the team before merging, since they affect multiple microservices at once