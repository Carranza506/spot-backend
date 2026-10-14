using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Business.Api.Migrations
{
    /// <inheritdoc />
    public partial class MapBusinessLocationToNtsPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema change (#58): business_locations.location was already geography(Point,4326).
            // Only the CLR type moved from string to NetTopologySuite's Point, and UseNetTopologySuite()
            // registers the postgis extension in the model — already created by InitialCreate, so
            // the resulting CREATE EXTENSION IF NOT EXISTS is a no-op.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other")
                .OldAnnotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
