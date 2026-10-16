# Build context is the repo root (see docker-compose.yml), so Spot.Shared is reachable.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Set by BuildKit to the image's target architecture (amd64, arm64). Every dotnet step below is
# scoped to it (`-a` maps amd64 to x64), so the publish output only carries the native libraries
# for that platform. Without it, SkiaSharp (#65) ships its native binaries for every OS and CPU
# (~470 MB), none of which but one would ever load.
ARG TARGETARCH
WORKDIR /src

# Restore first, using only the .csproj files, so this layer is cached across source changes.
COPY src/Spot.Shared/Spot.Shared.csproj src/Spot.Shared/
COPY src/Spot.Business.Api/Spot.Business.Api.csproj src/Spot.Business.Api/
RUN dotnet restore src/Spot.Business.Api/Spot.Business.Api.csproj -a "$TARGETARCH"

# Now bring in the actual source and build. Any compile error fails here, during
# `docker compose build`, not at container runtime.
COPY src/Spot.Shared/ src/Spot.Shared/
COPY src/Spot.Business.Api/ src/Spot.Business.Api/
RUN dotnet build src/Spot.Business.Api/Spot.Business.Api.csproj -c Release --no-restore -a "$TARGETARCH" --self-contained false

RUN dotnet publish src/Spot.Business.Api/Spot.Business.Api.csproj -c Release --no-build -a "$TARGETARCH" --self-contained false -o /app/publish

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

# Photo files (#65, PhotoStorage:RootPath). Created and handed to the non-root app user while
# still root, owner-only (0700): docker-compose.yml mounts a named volume here, and a new named
# volume copies this directory's ownership and mode, so the app can write to it without root.
ENV PhotoStorage__RootPath=/var/lib/spot/photos
RUN mkdir -p /var/lib/spot/photos \
    && chown "$APP_UID:$APP_UID" /var/lib/spot/photos \
    && chmod 700 /var/lib/spot/photos

# Run as the non-root "app" user baked into the base image instead of root (default).
USER $APP_UID

# Business.Api validates tokens (AddSpotJwtAuthentication), so it only ever needs the public key.
ENTRYPOINT ["/entrypoint-jwt.sh", "Spot.Business.Api.dll"]
