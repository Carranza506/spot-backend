using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Booking.Api.Data;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Services;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Repositories;

public sealed class BookingRepository(BookingDbContext db) : IBookingRepository
{
    /// <summary>The exclusion constraint created by the AddUserIdForeignKeyTriggerAndOverlapExclusion migration.</summary>
    public const string OverlapConstraint = "excl_active_booking_overlap";

    public async Task CreateAsync(BookingEntity booking, CancellationToken ct = default)
    {
        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.ExclusionViolation,
                                               ConstraintName: OverlapConstraint,
                                           })
        {
            throw new BookingSlotOverlapException(ex);
        }
    }

    public Task<BookingEntity?> GetByIdAsync(Guid bookingId, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    public async Task UpdateStatusAsync(BookingEntity booking, BookingStatus status, CancellationToken ct = default)
    {
        db.Bookings.Attach(booking);
        booking.Status = status;
        await db.SaveChangesAsync(ct);
        await db.Entry(booking).ReloadAsync(ct);
    }

    public Task<(IReadOnlyList<BookingEntity> Items, int Total)> ListByUserAsync(
        Guid userId, BookingStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Bookings.AsNoTracking().Where(b => b.UserId == userId);
        if (status is { } s)
            query = query.Where(b => b.Status == s);

        // Id breaks ties so paging is stable when two bookings start at the same time.
        return PageAsync(query.OrderByDescending(b => b.StartAt).ThenBy(b => b.Id), page, pageSize, ct);
    }

    public Task<(IReadOnlyList<BookingEntity> Items, int Total)> ListByBusinessAsync(
        Guid businessId, BookingStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtcExclusive,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Bookings.AsNoTracking().Where(b => b.BusinessId == businessId);
        if (status is { } s)
            query = query.Where(b => b.Status == s);
        if (fromUtc is { } from)
            query = query.Where(b => b.StartAt >= from);
        if (toUtcExclusive is { } to)
            query = query.Where(b => b.StartAt < to);

        return PageAsync(query.OrderBy(b => b.StartAt).ThenBy(b => b.Id), page, pageSize, ct);
    }

    private static async Task<(IReadOnlyList<BookingEntity> Items, int Total)> PageAsync(
        IOrderedQueryable<BookingEntity> query, int page, int pageSize, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);

        // Same as BusinessRepository: the offset is a long so a huge page can't overflow int into
        // a negative OFFSET (Postgres rejects that with a 500) or wrap to the wrong page. Past the
        // last row there's nothing to return, which also keeps the cast safe (offset < total).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
            return ([], total);

        var items = await query.Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
