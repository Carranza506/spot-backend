#!/bin/sh
# Bridges Docker Compose secrets (mounted as files) into the Jwt__* environment variables
# that JwtOptions/AddSpotJwtAuthentication expect, without ever baking key material into an
# image layer. JWT_PRIVATE_KEY_FILE / JWT_PUBLIC_KEY_FILE are set per-service in
# docker-compose.yml only for the services that actually need each key.
set -e

if [ -n "$JWT_PRIVATE_KEY_FILE" ] && [ -f "$JWT_PRIVATE_KEY_FILE" ]; then
    export Jwt__PrivateKeyPem="$(cat "$JWT_PRIVATE_KEY_FILE")"
fi

if [ -n "$JWT_PUBLIC_KEY_FILE" ] && [ -f "$JWT_PUBLIC_KEY_FILE" ]; then
    export Jwt__PublicKeyPem="$(cat "$JWT_PUBLIC_KEY_FILE")"
fi

exec dotnet "$@"
