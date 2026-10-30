using Spot.Booking.Api.Models;
using Spot.Booking.Api.Services;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Fakes;

/// <summary>Records what it was told, as (booking id, status) at the moment of the call.</summary>
public sealed class FakeBookingStatusNotifier : IBookingStatusNotifier
{
    private readonly List<(Guid BookingId, BookingStatus Status)> _notified = [];

    public IReadOnlyList<(Guid BookingId, BookingStatus Status)> Notified => _notified;

    public Task NotifyAsync(BookingEntity booking)
    {
        _notified.Add((booking.Id, booking.Status));
        return Task.CompletedTask;
    }
}
