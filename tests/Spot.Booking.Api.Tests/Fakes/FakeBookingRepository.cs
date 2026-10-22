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

    public Task<BookingEntity?> GetByIdAsync(Guid bookingId, CancellationToken ct = default) =>
        Task.FromResult(Created.FirstOrDefault(b => b.Id == bookingId));

    // Same filters, ordering and paging as BookingRepository's queries.
    public Task<(IReadOnlyList<BookingEntity> Items, int Total)> ListByUserAsync(
        Guid userId, BookingStatus? status, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(Page(
            Created.Where(b => b.UserId == userId && (status is null || b.Status == status))
                .OrderByDescending(b => b.StartAt).ThenBy(b => b.Id),
            page, pageSize));

    public Task<(IReadOnlyList<BookingEntity> Items, int Total)> ListByBusinessAsync(
        Guid businessId, BookingStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtcExclusive,
        int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(Page(
            Created.Where(b => b.BusinessId == businessId
                    && (status is null || b.Status == status)
                    && (fromUtc is null || b.StartAt >= fromUtc)
                    && (toUtcExclusive is null || b.StartAt < toUtcExclusive))
                .OrderBy(b => b.StartAt).ThenBy(b => b.Id),
            page, pageSize));

    private static (IReadOnlyList<BookingEntity> Items, int Total) Page(IEnumerable<BookingEntity> bookings, int page, int pageSize)
    {
        var all = bookings.ToList();
        return (all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count);
    }
}
