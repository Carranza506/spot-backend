using Spot.Booking.Api.Models;
using Spot.Booking.Api.Repositories;
using Spot.Booking.Api.Services;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IBookingRepository. Stores bookings in <paramref name="schedule"/>'s
/// booking list, so availability sees them, and emulates excl_active_booking_overlap: inserting an
/// active booking that overlaps (half-open) another active booking of the same business throws
/// <see cref="BookingSlotOverlapException"/>, as BookingRepository does on SQLSTATE 23P01.
/// </summary>
public sealed class FakeBookingRepository(FakeAvailabilityRepository schedule) : IBookingRepository
{
    public List<BookingEntity> Created { get; } = [];

    public void Reset() => Created.Clear();

    public Task CreateAsync(BookingEntity booking, CancellationToken ct = default)
    {
        if (AvailabilityCalculator.BlockingStatuses.Contains(booking.Status)
            && schedule.Bookings.Any(x => x.BusinessId == booking.BusinessId
                && AvailabilityCalculator.BlockingStatuses.Contains(x.Booking.Status)
                && x.Booking.StartAt < booking.EndAt
                && booking.StartAt < x.Booking.EndAt))
            throw new BookingSlotOverlapException();

        // The database defaults (gen_random_uuid(), CURRENT_TIMESTAMP).
        booking.Id = Guid.NewGuid();
        booking.CreatedAt = DateTimeOffset.UtcNow;
        booking.UpdatedAt = booking.CreatedAt;

        Created.Add(booking);
        schedule.Bookings.Add((booking.BusinessId, new BookedInterval(booking.StartAt, booking.EndAt, booking.Status)));
        return Task.CompletedTask;
    }
}
