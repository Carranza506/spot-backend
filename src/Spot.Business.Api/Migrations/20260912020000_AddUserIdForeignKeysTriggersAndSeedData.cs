using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Business.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdForeignKeysTriggersAndSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- user_id foreign keys to Spot.Auth.Api's "users" table (same shared database) ---

            // db/Spot.sql:56
            migrationBuilder.AddForeignKey(
                name: "FK_business_owners_users_user_id",
                table: "business_owners",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // db/Spot.sql:148
            migrationBuilder.AddForeignKey(
                name: "FK_favorite_businesses_users_user_id",
                table: "favorite_businesses",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // db/Spot.sql:155
            migrationBuilder.AddForeignKey(
                name: "FK_reviews_users_user_id",
                table: "reviews",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // EF Core's default convention adds an index on any FK property that isn't already
            // covered by another index — reviews.user_id had no index before this FK (unlike
            // business_owners/favorite_businesses, whose user_id is already covered by their
            // composite primary key). Missing this alongside the FK above is what caused
            // "pending model changes" when the live model was compared against the
            // hand-authored snapshot.
            migrationBuilder.CreateIndex(
                name: "IX_reviews_user_id",
                table: "reviews",
                column: "user_id");

            // --- "updated_at" triggers, reusing set_updated_at() from Spot.Auth.Api's
            // AddPgcryptoExtensionAndUsersUpdatedAtTrigger migration (must have already run). ---

            // db/Spot.sql:44
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_categories_updated_at BEFORE UPDATE ON categories FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // db/Spot.sql:52
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_businesses_updated_at BEFORE UPDATE ON businesses FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // db/Spot.sql:74
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_business_locations_updated_at BEFORE UPDATE ON business_locations FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // db/Spot.sql:88
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_services_updated_at BEFORE UPDATE ON services FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // db/Spot.sql:110
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_business_hours_updated_at BEFORE UPDATE ON business_hours FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // db/Spot.sql:119
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_schedule_exceptions_updated_at BEFORE UPDATE ON business_schedule_exceptions FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // db/Spot.sql:159
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_reviews_updated_at BEFORE UPDATE ON reviews FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // --- review/booking consistency trigger. References "bookings", owned by
            // Spot.Booking.Api but visible here because all services share one database. Only
            // checked at INSERT/UPDATE time on "reviews", so it does not require Spot.Booking.Api's
            // migrations to have already run (see README.md > Database for the recommended order
            // regardless). ---

            // db/Spot.sql:186-195. NOTE: Spot.sql compares against 'COMPLETED' (uppercase,
            // matching the C# BookingStatus.COMPLETED member name), but Npgsql's
            // HasPostgresEnum<T>() convention (see BookingDbContext.cs) actually creates the
            // "booking_status" Postgres enum type with lowercase labels — confirmed by this same
            // migration's own exclusion constraint, which needed 'pending'/'confirmed' for the
            // same reason. Using Spot.sql's literal casing here would make this check always
            // true (b.status would never equal 'COMPLETED'), silently disabling the guard, so
            // this uses the real lowercase value.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION validate_review() RETURNS TRIGGER LANGUAGE plpgsql AS $$
                DECLARE b RECORD;
                BEGIN
                 SELECT user_id,business_id,status INTO b FROM bookings WHERE id=NEW.booking_id;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Booking % does not exist',NEW.booking_id; END IF;
                 IF b.status<>'completed' THEN RAISE EXCEPTION 'A review can only be created for a completed booking'; END IF;
                 IF b.user_id<>NEW.user_id THEN RAISE EXCEPTION 'Review user does not match booking user'; END IF;
                 IF b.business_id<>NEW.business_id THEN RAISE EXCEPTION 'Review business does not match booking business'; END IF;
                 RETURN NEW;
                END; $$;
                """);

            // db/Spot.sql:196
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_validate_review BEFORE INSERT OR UPDATE ON reviews FOR EACH ROW EXECUTE FUNCTION validate_review();");

            // --- seed data (db/Spot.sql:208-213) ---
            // HasData needs stable, compile-time keys/values, so these pin fixed ids and a fixed
            // timestamp instead of gen_random_uuid()/CURRENT_TIMESTAMP (see
            // BusinessDbContext.SeedTimestamp).
            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "id", "name", "description", "is_active", "created_at", "updated_at" },
                values: new object[,]
                {
                    { Guid.Parse("6228d226-c50a-43af-bbae-835a224f0335"), "Belleza", "Servicios relacionados con belleza y cuidado personal", true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { Guid.Parse("fdc7b7cb-2529-4879-bf1c-ac82bb75d893"), "Salud", "Servicios relacionados con salud y bienestar", true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { Guid.Parse("ae084e20-06ce-407f-9bbd-be08089a407c"), "Deportes", "Servicios e instalaciones deportivas", true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { Guid.Parse("4a8d8583-9c61-44f5-93a6-19901475a98b"), "Restaurantes", "Restaurantes y establecimientos de comida", true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { Guid.Parse("045bfe83-fd2c-4694-906b-8c2e968ae188"), "Automotriz", "Servicios relacionados con vehículos", true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValues: new object[]
                {
                    Guid.Parse("6228d226-c50a-43af-bbae-835a224f0335"),
                    Guid.Parse("fdc7b7cb-2529-4879-bf1c-ac82bb75d893"),
                    Guid.Parse("ae084e20-06ce-407f-9bbd-be08089a407c"),
                    Guid.Parse("4a8d8583-9c61-44f5-93a6-19901475a98b"),
                    Guid.Parse("045bfe83-fd2c-4694-906b-8c2e968ae188"),
                });

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_validate_review ON reviews;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS validate_review();");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_reviews_updated_at ON reviews;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_schedule_exceptions_updated_at ON business_schedule_exceptions;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_business_hours_updated_at ON business_hours;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_services_updated_at ON services;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_business_locations_updated_at ON business_locations;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_businesses_updated_at ON businesses;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_categories_updated_at ON categories;");

            migrationBuilder.DropIndex(
                name: "IX_reviews_user_id",
                table: "reviews");

            migrationBuilder.DropForeignKey(
                name: "FK_reviews_users_user_id",
                table: "reviews");

            migrationBuilder.DropForeignKey(
                name: "FK_favorite_businesses_users_user_id",
                table: "favorite_businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_business_owners_users_user_id",
                table: "business_owners");
        }
    }
}
