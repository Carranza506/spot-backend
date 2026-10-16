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

    /// <summary>Seeds a service directly, bypassing CreateAsync — for update test setup.</summary>
    public ServiceEntity Seed(ServiceEntity service)
    {
        if (service.Id == Guid.Empty)
            service.Id = Guid.NewGuid();

        service.CreatedAt = DateTimeOffset.UtcNow;
        service.UpdatedAt = service.CreatedAt;
        _services[service.Id] = service;
        return service;
    }

    public Task<ServiceEntity?> GetTrackedByIdAsync(Guid serviceId, CancellationToken ct = default) =>
        Task.FromResult(_services.GetValueOrDefault(serviceId));

    public Task SaveChangesAsync(ServiceEntity service, CancellationToken ct = default)
    {
        service.UpdatedAt = DateTimeOffset.UtcNow;
        _services[service.Id] = service;
        return Task.CompletedTask;
    }
}
