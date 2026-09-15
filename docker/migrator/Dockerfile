# Needs the full SDK (not just the runtime) to run dotnet-ef. Build context is the repo root
# (see docker-compose.yml), so all 5 service projects + Spot.Shared are reachable.
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src

# dotnet-ef must match the Microsoft.EntityFrameworkCore(.Design/.Tools) package version every
# service references (see the <PackageReference> versions in each .csproj). Installed to a
# shared, world-executable path (not --global, which lands under /root and is unreachable once
# this container drops to the non-root $APP_UID user below).
RUN dotnet tool install dotnet-ef --version 9.0.7 --tool-path /dotnet-tools
ENV PATH="$PATH:/dotnet-tools"

# Restore first, using only the .csproj files, so this layer is cached across source changes.
COPY src/Spot.Shared/Spot.Shared.csproj src/Spot.Shared/
COPY src/Spot.Auth.Api/Spot.Auth.Api.csproj src/Spot.Auth.Api/
COPY src/Spot.Business.Api/Spot.Business.Api.csproj src/Spot.Business.Api/
COPY src/Spot.Booking.Api/Spot.Booking.Api.csproj src/Spot.Booking.Api/
COPY src/Spot.AiSearch.Api/Spot.AiSearch.Api.csproj src/Spot.AiSearch.Api/
COPY src/Spot.Notifications.Api/Spot.Notifications.Api.csproj src/Spot.Notifications.Api/

RUN dotnet restore src/Spot.Auth.Api/Spot.Auth.Api.csproj && \
    dotnet restore src/Spot.Business.Api/Spot.Business.Api.csproj && \
    dotnet restore src/Spot.Booking.Api/Spot.Booking.Api.csproj && \
    dotnet restore src/Spot.AiSearch.Api/Spot.AiSearch.Api.csproj && \
    dotnet restore src/Spot.Notifications.Api/Spot.Notifications.Api.csproj

# Now bring in the actual source and build every project. Any compile error fails here, during
# `docker compose build`, not while attempting a migration.
COPY src/Spot.Shared/ src/Spot.Shared/
COPY src/Spot.Auth.Api/ src/Spot.Auth.Api/
COPY src/Spot.Business.Api/ src/Spot.Business.Api/
COPY src/Spot.Booking.Api/ src/Spot.Booking.Api/
COPY src/Spot.AiSearch.Api/ src/Spot.AiSearch.Api/
COPY src/Spot.Notifications.Api/ src/Spot.Notifications.Api/

RUN dotnet build src/Spot.Auth.Api/Spot.Auth.Api.csproj -c Release --no-restore && \
    dotnet build src/Spot.Business.Api/Spot.Business.Api.csproj -c Release --no-restore && \
    dotnet build src/Spot.Booking.Api/Spot.Booking.Api.csproj -c Release --no-restore && \
    dotnet build src/Spot.AiSearch.Api/Spot.AiSearch.Api.csproj -c Release --no-restore && \
    dotnet build src/Spot.Notifications.Api/Spot.Notifications.Api.csproj -c Release --no-restore

COPY docker/migrator/migrate.sh /migrate.sh
RUN chmod +x /migrate.sh

# Run as the non-root "app" user baked into the base image instead of root (default). This is a
# short-lived, non-network-facing build-time job, but there's no cost to dropping privileges here.
# `dotnet ef` writes an MSBuild targets file into each project's obj/ on every invocation (even
# with --no-build), so /src must be owned by the user that will actually run it.
RUN chown -R $APP_UID:$APP_UID /src
USER $APP_UID

ENTRYPOINT ["/migrate.sh"]
