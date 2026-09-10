using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.AiSearch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRequestsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ai_requests_created_at",
                table: "ai_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_ai_requests_request_type_created_at",
                table: "ai_requests",
                columns: new[] { "request_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_requests_status_created_at",
                table: "ai_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_requests_user_id_created_at",
                table: "ai_requests",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ai_requests_created_at",
                table: "ai_requests");

            migrationBuilder.DropIndex(
                name: "IX_ai_requests_request_type_created_at",
                table: "ai_requests");

            migrationBuilder.DropIndex(
                name: "IX_ai_requests_status_created_at",
                table: "ai_requests");

            migrationBuilder.DropIndex(
                name: "IX_ai_requests_user_id_created_at",
                table: "ai_requests");
        }
    }
}
