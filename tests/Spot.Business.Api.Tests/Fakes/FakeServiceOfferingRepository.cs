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

    /// <summary>Seeds a service directly, bypassing CreateAsync — for list/get test setup.</summary>
    public ServiceEntity Seed(ServiceEntity service)
    {
        if (service.Id == Guid.Empty)
            service.Id = Guid.NewGuid();

        service.CreatedAt = DateTimeOffset.UtcNow;
        service.UpdatedAt = service.CreatedAt;
        _services[service.Id] = service;
        return service;
    }

    public Task<(IReadOnlyList<ServiceEntity> Items, int Total)> ListAsync(
        Guid businessId, int page, int pageSize, bool? active, CancellationToken ct = default)
    {
        // Mirrors ServiceOfferingRepository.ListAsync: business filter + optional active filter
        // (null = both), ordered by name then id, with the same overflow-safe offset.
        var all = _services.Values
            .Where(s => s.BusinessId == businessId && (active is not bool a || s.IsActive == a))
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .ToList();
        var offset = (long)(page - 1) * pageSize;
        var paged = offset >= all.Count ? [] : all.Skip((int)offset).Take(pageSize).ToList();

        return Task.FromResult(((IReadOnlyList<ServiceEntity>)paged, all.Count));
    }

    public Task<ServiceEntity?> GetByIdAsync(Guid serviceId, CancellationToken ct = default) =>
        Task.FromResult(_services.GetValueOrDefault(serviceId));
}
