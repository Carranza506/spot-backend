using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Business.Api.Migrations
{
    /// <summary>
    /// Intentionally empty: it only brings the model snapshot up to date with BookingReference (#70),
    /// which maps Spot.Booking.Api's "bookings" table read-only and is excluded from migrations.
    /// Without it, `dotnet ef database update` would refuse to run on pending model changes.
    /// </summary>
    public partial class AddBookingReferenceForReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
