using Microsoft.EntityFrameworkCore;
using Spot.Booking.Api.Data;
using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.Repositories;

public sealed class AvailabilityRepository(BookingDbContext db) : IAvailabilityRepository
{
    public Task<BusinessReference?> GetBusinessAsync(Guid businessId, CancellationToken ct = default) =>
        db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, ct);

    public Task<ServiceReference?> GetServiceAsync(Guid serviceId, CancellationToken ct = default) =>
        db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId, ct);

    public async Task<IReadOnlyList<BusinessHoursReference>> GetWeeklyHoursAsync(Guid businessId, CancellationToken ct = default) =>
        await db.BusinessHours.AsNoTracking().Where(h => h.BusinessId == businessId).ToListAsync(ct);

    public async Task<IReadOnlyList<BusinessScheduleExceptionReference>> GetScheduleExceptionsAsync(
        Guid businessId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await db.BusinessScheduleExceptions.AsNoTracking()
            .Where(e => e.BusinessId == businessId && e.ExceptionDate >= from && e.ExceptionDate <= to)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BookedInterval>> GetActiveBookingsAsync(
        Guid businessId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default) =>
        // Half-open overlap ([start, end), same as excl_active_booking_overlap's tstzrange '[)'):
        // a booking ending exactly at startUtc, or starting exactly at endUtc, isn't included.
        await db.Bookings.AsNoTracking()
            .Where(b => b.BusinessId == businessId
                && (b.Status == BookingStatus.PENDING || b.Status == BookingStatus.CONFIRMED)
                && b.StartAt < endUtc
                && b.EndAt > startUtc)
            .Select(b => new BookedInterval(b.StartAt, b.EndAt, b.Status))
            .ToListAsync(ct);
}
