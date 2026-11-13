using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Booking.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdForeignKeyTriggerAndOverlapExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // db/Spot.sql:2. Needed by the GIST exclusion constraint below (a "=" operator class
            // on a uuid column inside a GIST index requires btree_gist).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // db/Spot.sql:123 (plain REFERENCES, i.e. ON DELETE NO ACTION)
            migrationBuilder.AddForeignKey(
                name: "FK_bookings_users_user_id",
                table: "bookings",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.NoAction);

            // db/Spot.sql:145. Reuses set_updated_at() from Spot.Auth.Api's
            // AddPgcryptoExtensionAndUsersUpdatedAtTrigger migration (must have already run).
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_bookings_updated_at BEFORE UPDATE ON bookings FOR EACH ROW EXECUTE FUNCTION set_updated_at();");

            // db/Spot.sql:182-184. NOTE: Spot.sql's WHERE clause uses 'PENDING'/'CONFIRMED'
            // (uppercase, matching the C# enum member names), but that literally cannot match
            // any row here: Npgsql's HasPostgresEnum<T>() convention (see BookingDbContext.cs)
            // creates the "booking_status" Postgres enum type with lowercase labels
            // ("pending,confirmed,completed,cancelled,no_show" — see this migration's own
            // BookingStatus annotation, and identically for user_role/auth_provider/contact_type
            // in the other services). Using Spot.sql's literal casing here would silently exclude
            // zero rows instead of enforcing the overlap rule, so this uses the real
            // lowercase values that Npgsql actually stores.
            migrationBuilder.Sql("""
                ALTER TABLE bookings ADD CONSTRAINT excl_active_booking_overlap
                EXCLUDE USING GIST (business_id WITH =, tstzrange(start_at,end_at,'[)') WITH &&)
                WHERE(status IN ('pending','confirmed'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT IF EXISTS excl_active_booking_overlap;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_bookings_updated_at ON bookings;");

            migrationBuilder.DropForeignKey(
                name: "FK_bookings_users_user_id",
                table: "bookings");

            // No DROP EXTENSION btree_gist: harmless to leave installed, and safer than risking
            // a CASCADE drop if something else in the database starts depending on it later.
        }
    }
}
