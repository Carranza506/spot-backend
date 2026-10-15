using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Repositories;

namespace Spot.AiSearch.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IAiRequestRepository — no database involved, same idea as
/// Spot.Business.Api.Tests' Fakes. EF InMemory can't map AiRequest (JsonDocument/IPAddress
/// columns), so the service is tested against this instead. Records the token AddAsync received
/// and can be told to fail, to simulate a database error on insert.
/// </summary>
public sealed class FakeAiRequestRepository : IAiRequestRepository
{
    private readonly List<AiRequest> _rows = [];

    public IReadOnlyList<AiRequest> All => _rows;

    /// <summary>The token the last AddAsync call received.</summary>
    public CancellationToken? LastAddToken { get; private set; }

    /// <summary>When set, AddAsync throws it instead of storing the row, like a failed SaveChanges.</summary>
    public Exception? FailAddWith { get; set; }

    public Task AddAsync(AiRequest entity, CancellationToken ct = default)
    {
        LastAddToken = ct;
        if (FailAddWith is not null)
            throw FailAddWith;

        // EF generates the Guid key on Add; mirror that so callers see a non-empty id.
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        _rows.Add(entity);
        return Task.CompletedTask;
    }

    public Task<AiRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_rows.FirstOrDefault(x => x.Id == id));

    public Task<(IReadOnlyList<AiRequest> Items, int Total)> ListAsync(
        AiSearchFilterQuery filter, CancellationToken ct = default)
    {
        var query = _rows.AsEnumerable();

        if (filter.UserId.HasValue)
            query = query.Where(x => x.UserId == filter.UserId);
        if (filter.Status.HasValue)
            query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.RequestType.HasValue)
            query = query.Where(x => x.RequestType == filter.RequestType.Value);
        if (filter.From.HasValue)
            query = query.Where(x => x.CreatedAt >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(x => x.CreatedAt <= filter.To.Value);

        var all = query.OrderByDescending(x => x.CreatedAt).ToList();
        // Same long offset as AiRequestRepository: an int one overflows for a huge page.
        var offset = (long)(filter.Page - 1) * filter.PageSize;
        var page = offset >= all.Count ? [] : all.Skip((int)offset).Take(filter.PageSize).ToList();

        return Task.FromResult(((IReadOnlyList<AiRequest>)page, all.Count));
    }
}
