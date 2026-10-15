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

        // Offset as long so a huge page can't overflow int into a negative OFFSET (a 500 on
        // Postgres); past the last row there's nothing to return anyway, and a valid offset is
        // < total, so the Skip cast below is safe. Same as NotificationRepository.
        var offset = (long)(filter.Page - 1) * filter.PageSize;
        if (offset >= total)
            return ([], total);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((int)offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<AiRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.AiRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(AiRequest entity, CancellationToken ct = default)
    {
        db.AiRequests.Add(entity);
        await db.SaveChangesAsync(ct);
    }
}
