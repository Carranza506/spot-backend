# Build context is the repo root (see docker-compose.yml), so Spot.Shared is reachable.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, using only the .csproj files, so this layer is cached across source changes.
COPY src/Spot.Shared/Spot.Shared.csproj src/Spot.Shared/
COPY src/Spot.Auth.Api/Spot.Auth.Api.csproj src/Spot.Auth.Api/
RUN dotnet restore src/Spot.Auth.Api/Spot.Auth.Api.csproj

# Now bring in the actual source and build. Any compile error fails here, during
# `docker compose build`, not at container runtime.
COPY src/Spot.Shared/ src/Spot.Shared/
COPY src/Spot.Auth.Api/ src/Spot.Auth.Api/
RUN dotnet build src/Spot.Auth.Api/Spot.Auth.Api.csproj -c Release --no-restore

RUN dotnet publish src/Spot.Auth.Api/Spot.Auth.Api.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is only for the docker-compose.yml HTTP healthcheck against /health; the base image
# doesn't ship it. Installed while still root, before dropping privileges below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY docker/entrypoint-jwt.sh /entrypoint-jwt.sh
RUN chmod +x /entrypoint-jwt.sh

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Run as the non-root "app" user baked into the base image instead of root (default).
USER $APP_UID

# Auth.Api signs tokens, so it only ever needs the private key (see entrypoint-jwt.sh).
ENTRYPOINT ["/entrypoint-jwt.sh", "Spot.Auth.Api.dll"]
