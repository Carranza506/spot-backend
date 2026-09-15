#!/bin/sh
# Applies EF Core migrations for all 5 services against the `postgres` Compose service, in the
# order documented in README.md > "Database": Auth first (creates `users`, which every other
# service's migrations add a foreign key to), then Business (needs postgis), Booking (needs
# btree_gist), AiSearch, Notifications.
set -e

# AiSearch.Api's Program.cs calls AddSpotJwtAuthentication(...) unconditionally at startup
# (before builder.Build()), which validates Jwt:PublicKeyPem eagerly. `dotnet ef` executes that
# same top-level Program.cs to discover the DbContext, so the AiSearch.Api migration step below
# fails immediately with a config error unless this is set, even though it has nothing to do
# with the migration itself.
if [ -n "$JWT_PUBLIC_KEY_FILE" ] && [ -f "$JWT_PUBLIC_KEY_FILE" ]; then
    export Jwt__PublicKeyPem="$(cat "$JWT_PUBLIC_KEY_FILE")"
fi

run_migration() {
    project="$1"
    echo "==> dotnet ef database update --project $project"
    dotnet ef database update --project "$project" --configuration Release --no-build
    echo "==> $project migrations applied"
}

run_migration "src/Spot.Auth.Api"
run_migration "src/Spot.Business.Api"
run_migration "src/Spot.Booking.Api"
run_migration "src/Spot.AiSearch.Api"
run_migration "src/Spot.Notifications.Api"

echo "All 5 service migrations applied successfully."
