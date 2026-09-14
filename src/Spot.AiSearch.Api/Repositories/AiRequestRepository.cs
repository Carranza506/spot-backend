using Microsoft.EntityFrameworkCore;
using Spot.AiSearch.Api.Data;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.Repositories;

public class AiRequestRepository(AiSearchDbContext db) : IAiRequestRepository
{
    public async Task<(IReadOnlyList<AiRequest> Items, int Total)> ListAsync(
        AiSearchFilterQuery filter, CancellationToken ct = default)
    {
        var query = db.AiRequests.AsNoTracking();

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

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
