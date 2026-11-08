using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Repositories;

public sealed class ServiceOfferingRepository(BusinessDbContext db) : IServiceOfferingRepository
{
    public Task CreateAsync(ServiceEntity service, int? maxActiveServices, CancellationToken ct = default) =>
        InBusinessLockAsync(service.BusinessId, async () =>
        {
            if (maxActiveServices is { } max)
                await EnsureBelowLimitAsync(service.BusinessId, max, ct);

            db.Services.Add(service);
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task<ServiceEntity?> GetTrackedByIdAsync(Guid serviceId, CancellationToken ct = default) =>
        db.Services.FirstOrDefaultAsync(s => s.Id == serviceId, ct);

    public async Task SaveChangesAsync(ServiceEntity service, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await db.Entry(service).ReloadAsync(ct);
    }

    public Task SaveReactivationAsync(ServiceEntity service, int maxActiveServices, CancellationToken ct = default) =>
        InBusinessLockAsync(service.BusinessId, async () =>
        {
            // The count is a query, so it reads the stored rows, where this service is still inactive.
            await EnsureBelowLimitAsync(service.BusinessId, maxActiveServices, ct);
            await SaveChangesAsync(service, ct);
        }, ct);

    private async Task EnsureBelowLimitAsync(Guid businessId, int maxActiveServices, CancellationToken ct)
    {
        var active = await db.Services.CountAsync(s => s.BusinessId == businessId && s.IsActive, ct);
        if (active >= maxActiveServices)
            throw new ServiceLimitReachedException(maxActiveServices);
    }

    /// <summary>
    /// Runs <paramref name="write"/> in a transaction that first locks the business row, so concurrent
    /// creates and reactivations for the same business count its active services one at a time (same
    /// approach as PhotoRepository). The InMemory provider used by the unit tests has neither
    /// transactions nor row locks, so there the write simply runs as is.
    /// </summary>
    private async Task InBusinessLockAsync(Guid businessId, Func<Task> write, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await write();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Interpolated, so EF sends the id as a parameter, never as SQL text.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM businesses WHERE id = {businessId} FOR UPDATE", ct);
        await write();
        await transaction.CommitAsync(ct);
    }

    public async Task<(IReadOnlyList<ServiceEntity> Items, int Total)> ListAsync(
        Guid businessId, int page, int pageSize, bool? active, CancellationToken ct = default)
    {
        var query = db.Services.AsNoTracking().Where(s => s.BusinessId == businessId);
        if (active is { } a)
            query = query.Where(s => s.IsActive == a);

        var total = await query.CountAsync(ct);

        // Offset as long so a huge page can't overflow int into a negative OFFSET (same guard as
        // BusinessRepository.SearchAsync). Past the last row there's nothing to return.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
            return ([], total);

        var items = await query
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<ServiceEntity?> GetByIdAsync(Guid serviceId, CancellationToken ct = default) =>
        db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId, ct);
}
