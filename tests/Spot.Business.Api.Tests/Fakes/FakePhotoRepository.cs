using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IPhotoRepository — no database involved. Mirrors the real repository's
/// ordering (display_order, created_at, id), its business/service scoping, the photo limit and the
/// "one primary per business" rule. <see cref="FailNextAddWith"/> simulates an insert that fails
/// after the file was already stored (lost race on the limit, database error).
/// </summary>
public sealed class FakePhotoRepository : IPhotoRepository
{
    private readonly Dictionary<Guid, BusinessPhoto> _businessPhotos = [];
    private readonly Dictionary<Guid, ServicePhoto> _servicePhotos = [];
    private readonly Dictionary<Guid, ServiceEntity> _services = [];

    public IReadOnlyCollection<BusinessPhoto> BusinessPhotos => _businessPhotos.Values;
    public IReadOnlyCollection<ServicePhoto> ServicePhotos => _servicePhotos.Values;

    /// <summary>The next Add* call throws this instead of inserting.</summary>
    public Exception? FailNextAddWith { get; set; }

    public void Reset()
    {
        _businessPhotos.Clear();
        _servicePhotos.Clear();
        _services.Clear();
        FailNextAddWith = null;
    }

    /// <summary>Registers a service (with its <see cref="ServiceEntity.Business"/> set) for FindServiceAsync.</summary>
    public ServiceEntity SeedService(ServiceEntity service)
    {
        if (service.Id == Guid.Empty)
            service.Id = Guid.NewGuid();

        service.BusinessId = service.Business.Id;
        _services[service.Id] = service;
        return service;
    }

    public BusinessPhoto SeedBusinessPhoto(BusinessPhoto photo)
    {
        Stamp(photo);
        _businessPhotos[photo.Id] = photo;
        return photo;
    }

    public ServicePhoto SeedServicePhoto(ServicePhoto photo)
    {
        Stamp(photo);
        _servicePhotos[photo.Id] = photo;
        return photo;
    }

    // ---------- Business photos ----------

    public Task<(IReadOnlyList<BusinessPhoto> Items, int Total)> ListBusinessPhotosAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _businessPhotos.Values
            .Where(p => p.BusinessId == businessId)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToList();

        return Task.FromResult(Page(all, page, pageSize));
    }

    public Task<BusinessPhoto?> GetBusinessPhotoAsync(Guid businessId, Guid photoId, CancellationToken ct = default) =>
        Task.FromResult(_businessPhotos.GetValueOrDefault(photoId) is { } p && p.BusinessId == businessId ? p : null);

    public Task AddBusinessPhotoAsync(BusinessPhoto photo, int maxPhotos, CancellationToken ct = default)
    {
        ThrowIfFailing();
        if (_businessPhotos.Values.Count(p => p.BusinessId == photo.BusinessId) >= maxPhotos)
            throw new PhotoLimitReachedException(maxPhotos);

        if (photo.IsPrimary)
        {
            foreach (var other in _businessPhotos.Values.Where(p => p.BusinessId == photo.BusinessId))
                other.IsPrimary = false;
        }

        SeedBusinessPhoto(photo);
        return Task.CompletedTask;
    }

    public Task DeleteBusinessPhotoAsync(BusinessPhoto photo, CancellationToken ct = default)
    {
        _businessPhotos.Remove(photo.Id);
        return Task.CompletedTask;
    }

    // ---------- Service photos ----------

    public Task<ServiceEntity?> FindServiceAsync(Guid serviceId, CancellationToken ct = default) =>
        Task.FromResult(_services.GetValueOrDefault(serviceId));

    public Task<(IReadOnlyList<ServicePhoto> Items, int Total)> ListServicePhotosAsync(
        Guid serviceId, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _servicePhotos.Values
            .Where(p => p.ServiceId == serviceId)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToList();

        return Task.FromResult(Page(all, page, pageSize));
    }

    public Task<ServicePhoto?> GetServicePhotoAsync(Guid serviceId, Guid photoId, CancellationToken ct = default) =>
        Task.FromResult(_servicePhotos.GetValueOrDefault(photoId) is { } p && p.ServiceId == serviceId ? p : null);

    public Task AddServicePhotoAsync(ServicePhoto photo, int maxPhotos, CancellationToken ct = default)
    {
        ThrowIfFailing();
        if (_servicePhotos.Values.Count(p => p.ServiceId == photo.ServiceId) >= maxPhotos)
            throw new PhotoLimitReachedException(maxPhotos);

        SeedServicePhoto(photo);
        return Task.CompletedTask;
    }

    public Task DeleteServicePhotoAsync(ServicePhoto photo, CancellationToken ct = default)
    {
        _servicePhotos.Remove(photo.Id);
        return Task.CompletedTask;
    }

    // ---------- Helpers ----------

    private void ThrowIfFailing()
    {
        if (FailNextAddWith is { } failure)
        {
            FailNextAddWith = null;
            throw failure;
        }
    }

    private static void Stamp(BusinessPhoto photo)
    {
        if (photo.Id == Guid.Empty)
            photo.Id = Guid.NewGuid();
        if (photo.CreatedAt == default)
            photo.CreatedAt = DateTimeOffset.UtcNow;
    }

    private static void Stamp(ServicePhoto photo)
    {
        if (photo.Id == Guid.Empty)
            photo.Id = Guid.NewGuid();
        if (photo.CreatedAt == default)
            photo.CreatedAt = DateTimeOffset.UtcNow;
    }

    private static (IReadOnlyList<T> Items, int Total) Page<T>(List<T> all, int page, int pageSize)
    {
        var offset = (long)(page - 1) * pageSize;
        IReadOnlyList<T> items = offset >= all.Count ? [] : all.Skip((int)offset).Take(pageSize).ToList();
        return (items, all.Count);
    }
}
