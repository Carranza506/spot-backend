namespace Spot.Shared.Exceptions;

/// <summary>
/// Raised when the bookingId given doesn't exist: a booking read or status change in
/// Spot.Booking.Api, or a review in Spot.Business.Api. Controllers answer it with a 404.
/// </summary>
public sealed class BookingNotFoundException(Guid bookingId)
    : Exception($"Booking '{bookingId}' does not exist.")
{
    public Guid BookingId { get; } = bookingId;
}
