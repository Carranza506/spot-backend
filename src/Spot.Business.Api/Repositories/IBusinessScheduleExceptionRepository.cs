using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public interface IBusinessScheduleExceptionRepository
{
    /// <summary>
    /// Lists one business's schedule exceptions, paged and ordered by date ascending (unique per
    /// business, so pages are stable). <paramref name="from"/>/<paramref name="to"/> are inclusive;
    /// null means no bound on that side.
    /// </summary>
    Task<(IReadOnlyList<BusinessScheduleException> Items, int Total)> ListByBusinessAsync(
        Guid businessId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Loads an exception by id, but only if it belongs to <paramref name="businessId"/> — an
    /// exception of another business is reported as missing (null). The returned entity is tracked.
    /// </summary>
    Task<BusinessScheduleException?> GetByIdAsync(Guid businessId, Guid exceptionId, CancellationToken ct = default);

    /// <summary>Inserts <paramref name="exception"/>.</summary>
    /// <exception cref="ScheduleExceptionAlreadyExistsException">
    /// The business already has an exception on that date, including when a concurrent request
    /// inserts it first (duplicate key, SQLSTATE 23505).
    /// </exception>
    Task CreateAsync(BusinessScheduleException exception, CancellationToken ct = default);

    /// <summary>
    /// Saves the changes made to the tracked <paramref name="exception"/>, then refreshes it from the
    /// database (<c>updated_at</c> is set by a Postgres trigger).
    /// </summary>
    /// <exception cref="ScheduleExceptionAlreadyExistsException">
    /// Another exception of the business already has the new date, including when it's inserted
    /// concurrently (SQLSTATE 23505). Nothing is saved.
    /// </exception>
    Task UpdateAsync(BusinessScheduleException exception, CancellationToken ct = default);

    /// <summary>Hard delete — the row is removed permanently.</summary>
    Task DeleteAsync(BusinessScheduleException exception, CancellationToken ct = default);
}
