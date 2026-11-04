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

# Same bridge for the internal API key (#79), only set on the services that use it. Line endings are
# stripped because a key file edited on Windows would otherwise send an invalid header value.
if [ -n "$INTERNAL_API_KEY_FILE" ] && [ -f "$INTERNAL_API_KEY_FILE" ]; then
    export InternalApi__Key="$(tr -d '\r\n' < "$INTERNAL_API_KEY_FILE")"
fi

exec dotnet "$@"
