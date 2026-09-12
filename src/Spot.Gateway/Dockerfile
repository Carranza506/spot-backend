# Build context is the repo root (see docker-compose.yml). Spot.Gateway has no ProjectReference
# to Spot.Shared, but the pattern is kept identical to the other 5 services' Dockerfiles.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, using only the .csproj file, so this layer is cached across source changes.
COPY src/Spot.Gateway/Spot.Gateway.csproj src/Spot.Gateway/
RUN dotnet restore src/Spot.Gateway/Spot.Gateway.csproj

# Now bring in the actual source and build. Any compile error fails here, during
# `docker compose build`, not at container runtime.
COPY src/Spot.Gateway/ src/Spot.Gateway/
RUN dotnet build src/Spot.Gateway/Spot.Gateway.csproj -c Release --no-restore

RUN dotnet publish src/Spot.Gateway/Spot.Gateway.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is only for the docker-compose.yml HTTP healthcheck against /health; the base image
# doesn't ship it. Installed while still root, before dropping privileges below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Run as the non-root "app" user baked into the base image instead of root (default).
USER $APP_UID

ENTRYPOINT ["dotnet", "Spot.Gateway.dll"]
