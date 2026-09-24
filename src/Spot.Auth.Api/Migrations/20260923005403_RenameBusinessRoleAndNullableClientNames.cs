using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Auth.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameBusinessRoleAndNullableClientNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF/Npgsql's enum-annotation diffing can only ever emit "ALTER TYPE ... ADD VALUE"
            // for a label it doesn't recognize — it has no concept of a rename, so a plain
            // AlterDatabase().Annotation(...) here would add a brand-new 'business' label
            // alongside the untouched, now-orphaned 'business_owner' one instead of renaming it.
            // Postgres 10+ supports the actual rename directly.
            migrationBuilder.Sql("ALTER TYPE user_role RENAME VALUE 'business_owner' TO 'business';");

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            // A BUSINESS account has no person name (see AuthService.RegisterAsync /
            // UserProfileService.UpdateProfileAsync, which enforce this at the application level
            // too — this CHECK is the last line of defense, not the primary rejection path).
            migrationBuilder.Sql("""
                ALTER TABLE users ADD CONSTRAINT ck_users_client_names
                CHECK (role <> 'client'::user_role OR (first_name IS NOT NULL AND last_name IS NOT NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE users DROP CONSTRAINT IF EXISTS ck_users_client_names;");

            // Backfill first: a plain AlterColumn to NOT NULL would fail outright if any BUSINESS
            // row (necessarily NULL-named going into this rollback) still exists.
            migrationBuilder.Sql("UPDATE users SET first_name = '' WHERE first_name IS NULL;");
            migrationBuilder.Sql("UPDATE users SET last_name = '' WHERE last_name IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.Sql("ALTER TYPE user_role RENAME VALUE 'business' TO 'business_owner';");
        }
    }
}
