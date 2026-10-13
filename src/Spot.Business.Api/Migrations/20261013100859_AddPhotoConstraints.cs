using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Business.Api.Migrations
{
    /// <summary>
    /// Photo constraints for #65:
    /// <list type="bullet">
    /// <item>ux_business_photos_one_primary: at most one primary photo per business (partial unique index).</item>
    /// <item>CHECK (display_order >= 0) on both photo tables. db/Spot.sql always had it, but the
    /// earlier migrations never created it; the names match the ones Postgres generates there.</item>
    /// </list>
    /// Existing rows are normalized first, so the constraints can't fail on a database with data.
    /// </summary>
    public partial class AddPhotoConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep only the most recent primary photo of each business (ties broken by id).
            migrationBuilder.Sql(
                """
                UPDATE business_photos AS bp
                SET is_primary = FALSE
                WHERE bp.is_primary
                  AND EXISTS (
                      SELECT 1
                      FROM business_photos AS newer
                      WHERE newer.business_id = bp.business_id
                        AND newer.is_primary
                        AND (newer.created_at, newer.id) > (bp.created_at, bp.id));
                """);
            migrationBuilder.Sql("UPDATE business_photos SET display_order = 0 WHERE display_order < 0;");
            migrationBuilder.Sql("UPDATE service_photos SET display_order = 0 WHERE display_order < 0;");

            migrationBuilder.CreateIndex(
                name: "ux_business_photos_one_primary",
                table: "business_photos",
                column: "business_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.AddCheckConstraint(
                name: "business_photos_display_order_check",
                table: "business_photos",
                sql: "display_order >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "service_photos_display_order_check",
                table: "service_photos",
                sql: "display_order >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The data normalization in Up isn't reverted: there's no record of the previous values,
            // and the normalized data is valid without the constraints too.
            migrationBuilder.DropCheckConstraint(
                name: "service_photos_display_order_check",
                table: "service_photos");

            migrationBuilder.DropCheckConstraint(
                name: "business_photos_display_order_check",
                table: "business_photos");

            migrationBuilder.DropIndex(
                name: "ux_business_photos_one_primary",
                table: "business_photos");
        }
    }
}
