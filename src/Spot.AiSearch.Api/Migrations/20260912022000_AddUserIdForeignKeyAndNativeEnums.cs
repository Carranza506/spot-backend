using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.AiSearch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdForeignKeyAndNativeEnums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // db/Spot.sql:162
            migrationBuilder.AddForeignKey(
                name: "FK_ai_requests_users_user_id",
                table: "ai_requests",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // db/Spot.sql:8-9. Labels are UPPERCASE to match the existing text data (written as
            // enumValue.ToString(), e.g. "SUCCESS") so the USING casts below are lossless, and to
            // match Spot.sql exactly (see AiSearchDbContext.cs for why this does not follow the
            // lowercase convention used by the other enums in this codebase).
            migrationBuilder.Sql("CREATE TYPE ai_request_status AS ENUM ('SUCCESS', 'ERROR', 'TIMEOUT');");
            migrationBuilder.Sql("CREATE TYPE ai_request_type AS ENUM ('BUSINESS_SEARCH', 'GENERAL_QUERY', 'OTHER');");

            migrationBuilder.Sql(
                "ALTER TABLE ai_requests ALTER COLUMN status TYPE ai_request_status USING status::ai_request_status;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_requests ALTER COLUMN request_type TYPE ai_request_type USING request_type::ai_request_type;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE ai_requests ALTER COLUMN request_type TYPE text USING request_type::text;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_requests ALTER COLUMN status TYPE text USING status::text;");

            migrationBuilder.Sql("DROP TYPE IF EXISTS ai_request_type;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS ai_request_status;");

            migrationBuilder.DropForeignKey(
                name: "FK_ai_requests_users_user_id",
                table: "ai_requests");
        }
    }
}
