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

The full schema lives in `db/Spot.sql`. To create the local database:

```bash
psql -U postgres -c "CREATE DATABASE spot_dev;"
psql -U postgres -d spot_dev -f db/Spot.sql
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

If `spot_dev` was created from `db/Spot.sql`, the `users`/`refresh_tokens` tables (and the
`user_role` enum) already exist — `Spot.Auth.Api`'s `InitialCreate` migration detects and skips
re-creating the enum, but do **not** also run `dotnet ef database update` against that same
database, since it would try to re-create the tables themselves and fail. Migrations are meant for
a database that doesn't already have them (e.g. a fresh `spot_dev` created without running
`db/Spot.sql`, or CI):

```bash
dotnet ef database update
```

In production, set the connection string the same way the JWT keys are set — an environment
variable ASP.NET Core maps automatically: `ConnectionStrings__DefaultConnection`.

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