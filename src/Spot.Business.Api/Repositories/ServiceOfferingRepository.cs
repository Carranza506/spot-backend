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

    public Task<ServiceEntity?> GetTrackedByIdAsync(Guid serviceId, CancellationToken ct = default) =>
        db.Services.FirstOrDefaultAsync(s => s.Id == serviceId, ct);

    public async Task SaveChangesAsync(ServiceEntity service, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await db.Entry(service).ReloadAsync(ct);
    }
}
