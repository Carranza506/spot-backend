namespace Spot.Business.Api.Services;

/// <summary>Raised when the caller isn't the booking's client (bookings.user_id != JWT sub).</summary>
public sealed class BookingAccessDeniedException(Guid bookingId, Guid callerId)
    : Exception($"Account '{callerId}' is not the client of booking '{bookingId}'.")
{
    public Guid BookingId { get; } = bookingId;
    public Guid CallerId { get; } = callerId;
}
