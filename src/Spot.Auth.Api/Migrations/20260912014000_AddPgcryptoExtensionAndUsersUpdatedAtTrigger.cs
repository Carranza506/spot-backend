using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Auth.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPgcryptoExtensionAndUsersUpdatedAtTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // db/Spot.sql:3
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            // db/Spot.sql:13-14. Shared by every service's "updated_at" triggers (see the
            // matching migrations in Spot.Business.Api and Spot.Booking.Api). Spot.Auth.Api
            // owns this function because it is the first service in the required migration
            // order (see README.md > Database) and is itself the first consumer, via the
            // trigger below. IMPORTANT: because other services' triggers depend on this
            // function, this migration's Down() must be the LAST one rolled back across all
            // five services, or their DROP TRIGGER statements will run against a
            // still-existing function while this Down() (running before them) would otherwise
            // fail on DROP FUNCTION with dependent objects still attached.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION set_updated_at() RETURNS TRIGGER LANGUAGE plpgsql AS $$
                BEGIN NEW.updated_at=CURRENT_TIMESTAMP; RETURN NEW; END; $$;
                """);

            // db/Spot.sql:23
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_users_updated_at BEFORE UPDATE ON users FOR EACH ROW EXECUTE FUNCTION set_updated_at();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_users_updated_at ON users;");

            // No CASCADE: if another service's trigger still references this function, this
            // intentionally fails loudly instead of silently dropping their trigger too. Roll
            // back Spot.Business.Api's and Spot.Booking.Api's "updated_at" trigger migrations
            // before this one.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS set_updated_at();");

            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pgcrypto;");
        }
    }
}
