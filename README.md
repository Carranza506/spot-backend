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

_(Pending: a `docker-compose.yml` will be added to spin up all 6 services + Postgres with a single `docker compose up` command)._

In the meantime, open one terminal per service and run `dotnet run --project src/<Service>` in each.

## Team conventions

- Branches: `feature/short-name`, `fix/short-name`
- Commits: short, descriptive messages in English (keep it consistent across the team)
- Pull requests: at least 1 reviewer before merging to `main`
- Changes to `Spot.Shared` (shared DTOs/enums) should be flagged to the team before merging, since they affect multiple microservices at once