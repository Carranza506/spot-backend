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
}
