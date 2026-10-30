using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Services;

/// <summary>Tells Spot.Notifications.Api that a booking's status changed (#79).</summary>
public interface IBookingStatusNotifier
{
    /// <summary>
    /// Best-effort: never throws, and a failure is only logged. Takes no CancellationToken on
    /// purpose: it runs after the transition is committed, so a client disconnecting must not
    /// cancel it. Its own short timeout bounds the wait.
    /// </summary>
    Task NotifyAsync(BookingEntity booking);
}
