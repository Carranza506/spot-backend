using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public sealed class ReviewRepository(BusinessDbContext db) : IReviewRepository
{
    public Task<BookingReference?> GetBookingAsync(Guid bookingId, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    public async Task CreateAsync(Review review, CancellationToken ct = default)
    {
        db.Reviews.Add(review);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        // Matched by SQLSTATE only, like favorites: the unique index is IX_reviews_booking_id in the
        // migration but reviews_booking_id_key in db/Spot.sql. booking_id is the only unique column.
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(review).State = EntityState.Detached;
            throw new ReviewAlreadyExistsException(review.BookingId, ex);
        }
        // trg_validate_review's RAISE EXCEPTION (default SQLSTATE P0001, raise_exception).
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.RaiseException })
        {
            db.Entry(review).State = EntityState.Detached;
            throw new BookingNotReviewableException(review.BookingId, ex);
        }
    }

    public async Task<(IReadOnlyList<Review> Items, int Total)> ListByBusinessAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Reviews.AsNoTracking().Where(r => r.BusinessId == businessId);
        var total = await query.CountAsync(ct);

        // Same as BusinessRepository: a long offset can't overflow, and past the last row there's nothing to read.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
            return ([], total);

        // Id breaks ties so pages are stable when two reviews share a created_at.
        var items = await query
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((int)offset).Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }
}
