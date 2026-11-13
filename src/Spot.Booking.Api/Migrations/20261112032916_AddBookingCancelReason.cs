using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Booking.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCancelReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #163: the optional reason given to cancelBooking (db/Spot.sql, bookings.cancel_reason).
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "bookings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancel_reason",
                table: "bookings");
        }
    }
}
