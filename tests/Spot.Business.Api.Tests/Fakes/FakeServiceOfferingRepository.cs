using Spot.Business.Api.Repositories;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IServiceOfferingRepository — no database involved. Shared between
/// ServiceOfferingServiceTests (direct construction) and BusinessesApiFactory (DI-swapped), same
/// as FakeBusinessRepository.
/// </summary>
public sealed class FakeServiceOfferingRepository : IServiceOfferingRepository
{
    private readonly Dictionary<Guid, ServiceEntity> _services = [];

    public IReadOnlyCollection<ServiceEntity> All => _services.Values;

    public void Reset() => _services.Clear();

    public Task CreateAsync(ServiceEntity service, CancellationToken ct = default)
    {
        service.Id = Guid.NewGuid();
        service.CreatedAt = DateTimeOffset.UtcNow;
        service.UpdatedAt = service.CreatedAt;
        _services[service.Id] = service;

        return Task.CompletedTask;
    }
}
