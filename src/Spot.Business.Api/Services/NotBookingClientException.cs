namespace Spot.Business.Api.Services;

/// <summary>
/// Raised when the caller isn't the booking's client (bookings.user_id != JWT sub), so it can't
/// review it (#70). Not the same as Spot.Booking.Api's BookingAccessDeniedException, which also lets
/// the business's owner and a SUPERADMIN through.
/// </summary>
public sealed class NotBookingClientException(Guid bookingId, Guid callerId)
    : Exception($"Account '{callerId}' is not the client of booking '{bookingId}'.")
{
    public Guid BookingId { get; } = bookingId;
    public Guid CallerId { get; } = callerId;
}
