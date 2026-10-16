using Spot.Business.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

/// <summary>An uploaded photo as the controller received it. <see cref="Content"/> is untrusted.</summary>
public sealed record PhotoUpload(Stream Content, int DisplayOrder, bool IsPrimary = false);

/// <summary>A stored photo file to send back as is. The caller disposes <see cref="Content"/>.</summary>
public sealed record PhotoFile(Stream Content, string ContentType);

/// <summary>
/// Business and service photos (#65). Ownership follows #56: the caller (JWT sub) must own the
/// business, or the business the service belongs to. Public reads only show active businesses and
/// services; an owner can still manage the photos of an inactive one, same as PATCH.
/// </summary>
public interface IPhotoService
{
    // ---------- Business photos ----------

    /// <summary>Public listing. Null if the business doesn't exist or is inactive (404).</summary>
    Task<PaginatedResponse<PhotoDto>?> ListBusinessPhotosAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <exception cref="BusinessNotFoundException">No such business.</exception>
    /// <exception cref="BusinessAccessDeniedException">The caller doesn't own it.</exception>
    /// <exception cref="Photos.InvalidPhotoException">The upload isn't an acceptable image.</exception>
    /// <exception cref="Repositories.PhotoLimitReachedException">The business already has the maximum number of photos.</exception>
    Task<PhotoDto> UploadBusinessPhotoAsync(
        Guid businessId, Guid callerId, PhotoUpload upload, CancellationToken ct = default);

    /// <summary>False if the photo doesn't exist or belongs to another business (404).</summary>
    /// <exception cref="BusinessNotFoundException">No such business.</exception>
    /// <exception cref="BusinessAccessDeniedException">The caller doesn't own it.</exception>
    Task<bool> DeleteBusinessPhotoAsync(Guid businessId, Guid photoId, Guid callerId, CancellationToken ct = default);

    // ---------- Service photos ----------

    /// <summary>Public listing. Null if the service doesn't exist, is inactive, or its business is inactive (404).</summary>
    Task<PaginatedResponse<PhotoDto>?> ListServicePhotosAsync(
        Guid serviceId, int page, int pageSize, CancellationToken ct = default);

    /// <exception cref="ServiceNotFoundException">No such service.</exception>
    /// <exception cref="BusinessAccessDeniedException">The caller doesn't own the service's business.</exception>
    /// <exception cref="Photos.InvalidPhotoException">The upload isn't an acceptable image.</exception>
    /// <exception cref="Repositories.PhotoLimitReachedException">The service already has the maximum number of photos.</exception>
    Task<PhotoDto> UploadServicePhotoAsync(
        Guid serviceId, Guid callerId, PhotoUpload upload, CancellationToken ct = default);

    /// <summary>False if the photo doesn't exist or belongs to another service (404).</summary>
    /// <exception cref="ServiceNotFoundException">No such service.</exception>
    /// <exception cref="BusinessAccessDeniedException">The caller doesn't own the service's business.</exception>
    Task<bool> DeleteServicePhotoAsync(Guid serviceId, Guid photoId, Guid callerId, CancellationToken ct = default);

    // ---------- Files ----------

    /// <summary>
    /// The stored file behind a <c>Photo.url</c>, or null if <paramref name="fileName"/> isn't a valid
    /// storage key or there's no such file (deleted photo).
    /// </summary>
    Task<PhotoFile?> OpenPhotoFileAsync(string fileName, CancellationToken ct = default);
}
