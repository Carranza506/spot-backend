using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Repositories;

public sealed class ServiceOfferingRepository(BusinessDbContext db) : IServiceOfferingRepository
{
    public async Task CreateAsync(ServiceEntity service, CancellationToken ct = default)
    {
        db.Services.Add(service);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<ServiceEntity> Items, int Total)> ListAsync(
        Guid businessId, int page, int pageSize, bool active, CancellationToken ct = default)
    {
        var query = db.Services.AsNoTracking().Where(s => s.BusinessId == businessId && s.IsActive == active);

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
