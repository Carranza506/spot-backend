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

The full schema lives in `db/spot_database_postgresql_18.sql]`. To create the local database:

```bash
psql -U postgres -c "CREATE DATABASE spot_dev;"
psql -U postgres -d spot_dev -f db/spot_database_postgresql_18.sql
```

Each service that needs data access uses its own connection string, configured in `appsettings.Development.json` **(not committed, see the environment variables section below)**.

## Environment variables / local configuration

Every project that needs one ships an `appsettings.Example.json` as a template. Before running a service:

```bash
cp src/Spot.Auth.Api/appsettings.Example.json src/Spot.Auth.Api/appsettings.Development.json
```

Then fill in the real values there (connection string, JWT secret, API keys). **Never commit `appsettings.Development.json` or real keys** — it's already in `.gitignore`.

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