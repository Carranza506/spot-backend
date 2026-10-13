using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public sealed class BusinessScheduleExceptionRepository(BusinessDbContext db) : IBusinessScheduleExceptionRepository
{
    public async Task<(IReadOnlyList<BusinessScheduleException> Items, int Total)> ListByBusinessAsync(
        Guid businessId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.BusinessScheduleExceptions.AsNoTracking().Where(e => e.BusinessId == businessId);

        if (from is not null)
            query = query.Where(e => e.ExceptionDate >= from);

        if (to is not null)
            query = query.Where(e => e.ExceptionDate <= to);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.ExceptionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<BusinessScheduleException?> GetByIdAsync(Guid businessId, Guid exceptionId, CancellationToken ct = default) =>
        db.BusinessScheduleExceptions.FirstOrDefaultAsync(e => e.Id == exceptionId && e.BusinessId == businessId, ct);

    public async Task CreateAsync(BusinessScheduleException exception, CancellationToken ct = default)
    {
        // Up-front check for a clean 409 in the common case — see the catch below for the
        // race-proof fallback (the same date inserted concurrently, after this check passed).
        if (await db.BusinessScheduleExceptions.AnyAsync(
                e => e.BusinessId == exception.BusinessId && e.ExceptionDate == exception.ExceptionDate, ct))
            throw new ScheduleExceptionAlreadyExistsException(exception.BusinessId, exception.ExceptionDate);

        db.BusinessScheduleExceptions.Add(exception);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Matched by SQLSTATE only, same as BusinessRepository.AddFavoriteAsync: the unique key is
            // the idx_schedule_exceptions_business_date index in the migration but an unnamed
            // UNIQUE(business_id,exception_date) constraint in db/Spot.sql. The id is generated, so
            // it's the only unique key an insert here can break.
            db.Entry(exception).State = EntityState.Detached;
            throw new ScheduleExceptionAlreadyExistsException(exception.BusinessId, exception.ExceptionDate);
        }
    }

    public async Task DeleteAsync(BusinessScheduleException exception, CancellationToken ct = default)
    {
        db.BusinessScheduleExceptions.Remove(exception);
        await db.SaveChangesAsync(ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
