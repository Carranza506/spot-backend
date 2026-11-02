namespace Spot.Business.Api.Models;

/// <summary>
/// Minimal read-only reference to the `bookings` table, which lives in the same shared Postgres
/// database but is owned and migrated exclusively by Spot.Booking.Api. Mapped here, with migrations
/// excluded, so a review (#70) can check that its booking exists and belongs to the caller, and take
/// the business from it. The booking's status isn't mapped: trg_validate_review checks it on insert.
/// </summary>
public class BookingReference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
}
