using Spot.Business.Api.Models;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Repositories;

/// <summary>
/// Persistence for business_photos and service_photos (#65). Only rows: the files themselves are
/// handled by <see cref="Storage.IPhotoStorage"/> in the service layer.
/// </summary>
/// <remarks>
/// Adds run in a transaction that first locks the parent row (the business or the service) with
/// SELECT ... FOR UPDATE. Concurrent uploads to the same parent then run one after the other, so
/// the photo limit can't be exceeded by two requests that both counted one below it, and the
/// previous primary photo is always unset before the new one is inserted.
/// </remarks>
public interface IPhotoRepository
{
    // ---------- Business photos ----------

    /// <summary>
    /// One page of <paramref name="businessId"/>'s photos, ordered by display_order, then
    /// created_at, then id (so pages are stable). Not tracked.
    /// </summary>
    Task<(IReadOnlyList<BusinessPhoto> Items, int Total)> ListBusinessPhotosAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// A photo of <paramref name="businessId"/>, or null if it doesn't exist or belongs to another
    /// business. Tracked, so it can be passed to <see cref="DeleteBusinessPhotoAsync"/>.
    /// </summary>
    Task<BusinessPhoto?> GetBusinessPhotoAsync(Guid businessId, Guid photoId, CancellationToken ct = default);

    /// <summary>
    /// Inserts <paramref name="photo"/>. When it's primary, the business's current primary photo
    /// is unset in the same transaction. Callers are expected to have checked the business exists
    /// and belongs to the caller.
    /// </summary>
    /// <exception cref="PhotoLimitReachedException">The business already has <paramref name="maxPhotos"/> photos.</exception>
    Task AddBusinessPhotoAsync(BusinessPhoto photo, int maxPhotos, CancellationToken ct = default);

    /// <summary>Hard delete of the row. The file is the caller's to remove.</summary>
    Task DeleteBusinessPhotoAsync(BusinessPhoto photo, CancellationToken ct = default);

    // ---------- Service photos ----------

    /// <summary>
    /// The service with <see cref="ServiceEntity.Business"/> loaded (for the ownership and
    /// visibility checks), or null if there's no such service. Not tracked.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in <see cref="IServiceOfferingRepository"/> so photos don't overlap
    /// with the service CRUD stories (#114-#116) that extend that repository.
    /// </remarks>
    Task<ServiceEntity?> FindServiceAsync(Guid serviceId, CancellationToken ct = default);

    /// <summary>Same as <see cref="ListBusinessPhotosAsync"/>, for a service's photos.</summary>
    Task<(IReadOnlyList<ServicePhoto> Items, int Total)> ListServicePhotosAsync(
        Guid serviceId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Same as <see cref="GetBusinessPhotoAsync"/>, scoped to <paramref name="serviceId"/>.</summary>
    Task<ServicePhoto?> GetServicePhotoAsync(Guid serviceId, Guid photoId, CancellationToken ct = default);

    /// <summary>Inserts <paramref name="photo"/>. Service photos have no primary flag.</summary>
    /// <exception cref="PhotoLimitReachedException">The service already has <paramref name="maxPhotos"/> photos.</exception>
    Task AddServicePhotoAsync(ServicePhoto photo, int maxPhotos, CancellationToken ct = default);

    /// <summary>Hard delete of the row. The file is the caller's to remove.</summary>
    Task DeleteServicePhotoAsync(ServicePhoto photo, CancellationToken ct = default);
}
