namespace Spot.Business.Api.Services;

/// <summary>Raised when a review (#70) targets a booking that doesn't exist.</summary>
public sealed class BookingNotFoundException(Guid bookingId)
    : Exception($"Booking '{bookingId}' does not exist.")
{
    public Guid BookingId { get; } = bookingId;
}
