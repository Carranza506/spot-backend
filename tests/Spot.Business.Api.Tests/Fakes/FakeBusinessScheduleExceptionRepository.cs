using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IBusinessScheduleExceptionRepository — no database involved. Mirrors the
/// real repository's ordering (date ascending), inclusive from/to filter, business scoping on
/// lookups, and its one-exception-per-date rule on create.
/// </summary>
public sealed class FakeBusinessScheduleExceptionRepository : IBusinessScheduleExceptionRepository
{
    private readonly Dictionary<Guid, BusinessScheduleException> _exceptions = [];

    public IReadOnlyCollection<BusinessScheduleException> All => _exceptions.Values;

    public void Reset() => _exceptions.Clear();

    /// <summary>Seeds an exception directly, bypassing CreateAsync — for test setup.</summary>
    public BusinessScheduleException Seed(BusinessScheduleException exception)
    {
        if (exception.Id == Guid.Empty)
            exception.Id = Guid.NewGuid();

        exception.CreatedAt = DateTimeOffset.UtcNow;
        exception.UpdatedAt = exception.CreatedAt;
        _exceptions[exception.Id] = exception;
        return exception;
    }

    public Task<(IReadOnlyList<BusinessScheduleException> Items, int Total)> ListByBusinessAsync(
        Guid businessId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _exceptions.Values
            .Where(e => e.BusinessId == businessId)
            .Where(e => from is null || e.ExceptionDate >= from)
            .Where(e => to is null || e.ExceptionDate <= to)
            .OrderBy(e => e.ExceptionDate)
            .ToList();

        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(((IReadOnlyList<BusinessScheduleException>)pageItems, all.Count));
    }

    public Task<BusinessScheduleException?> GetByIdAsync(Guid businessId, Guid exceptionId, CancellationToken ct = default) =>
        Task.FromResult(_exceptions.TryGetValue(exceptionId, out var e) && e.BusinessId == businessId ? e : null);

    public Task CreateAsync(BusinessScheduleException exception, CancellationToken ct = default)
    {
        if (_exceptions.Values.Any(e => e.BusinessId == exception.BusinessId && e.ExceptionDate == exception.ExceptionDate))
            throw new ScheduleExceptionAlreadyExistsException(exception.BusinessId, exception.ExceptionDate);

        exception.Id = Guid.NewGuid();
        Seed(exception);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(BusinessScheduleException exception, CancellationToken ct = default)
    {
        _exceptions.Remove(exception.Id);
        return Task.CompletedTask;
    }
}
