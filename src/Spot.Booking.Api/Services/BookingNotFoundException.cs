namespace Spot.Booking.Api.Services;

/// <summary>Raised when the bookingId given doesn't exist.</summary>
public sealed class BookingNotFoundException(Guid bookingId)
    : Exception($"Booking '{bookingId}' does not exist.")
{
    public Guid BookingId { get; } = bookingId;
}
