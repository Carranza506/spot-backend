using Microsoft.Extensions.Options;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Options;
using Spot.Business.Api.Photos;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Storage;
using Spot.Shared.Pagination;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Services;

/// <remarks>
/// An upload goes through the cheap checks before the expensive ones, so a caller who doesn't own
/// the business, or one that's already at its limit, never gets an image decoded:
/// ownership → photo limit (fast pre-check) → decode and re-encode → write the file → insert the
/// row (which re-checks the limit under a row lock). If the insert fails for any reason, the file
/// just written is deleted again, so no stored file is left without a row.
/// </remarks>
public sealed class PhotoService(
    IBusinessRepository businessRepository,
    IPhotoRepository photoRepository,
    IPhotoImageProcessor imageProcessor,
    IPhotoStorage storage,
    IOptions<PhotoLimitsOptions> limits,
    ILogger<PhotoService> logger) : IPhotoService
{
    // ---------- Business photos ----------

    public async Task<PaginatedResponse<PhotoDto>?> ListBusinessPhotosAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        // Same visibility as GET /business/businesses/{businessId}: inactive is reported as missing.
        var business = await businessRepository.GetByIdAsync(businessId, ct);
        if (business is not { IsActive: true })
            return null;

        var (items, total) = await photoRepository.ListBusinessPhotosAsync(businessId, page, pageSize, ct);
        return ToPage(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<PhotoDto> UploadBusinessPhotoAsync(
        Guid businessId, Guid callerId, PhotoUpload upload, CancellationToken ct = default)
    {
        await RequireOwnedBusinessAsync(businessId, callerId, ct);

        var maxPhotos = limits.Value.MaxPerBusiness;
        var (_, total) = await photoRepository.ListBusinessPhotosAsync(businessId, page: 1, pageSize: 1, ct);
        if (total >= maxPhotos)
            throw new PhotoLimitReachedException(maxPhotos);

        var photo = new BusinessPhoto
        {
            BusinessId = businessId,
            IsPrimary = upload.IsPrimary,
            DisplayOrder = upload.DisplayOrder,
        };
        await StoreAsync(upload.Content, key =>
        {
            photo.StorageKey = key;
            return photoRepository.AddBusinessPhotoAsync(photo, maxPhotos, ct);
        }, ct);

        logger.LogInformation("Photo {PhotoId} uploaded for business {BusinessId}.", photo.Id, businessId);
        return ToDto(photo);
    }

    public async Task<bool> DeleteBusinessPhotoAsync(
        Guid businessId, Guid photoId, Guid callerId, CancellationToken ct = default)
    {
        await RequireOwnedBusinessAsync(businessId, callerId, ct);

        var photo = await photoRepository.GetBusinessPhotoAsync(businessId, photoId, ct);
        if (photo is null)
            return false;

        await photoRepository.DeleteBusinessPhotoAsync(photo, ct);
        await DeleteFileAsync(photo.StorageKey);

        logger.LogInformation("Photo {PhotoId} deleted from business {BusinessId}.", photo.Id, businessId);
        return true;
    }

    // ---------- Service photos ----------

    public async Task<PaginatedResponse<PhotoDto>?> ListServicePhotosAsync(
        Guid serviceId, int page, int pageSize, CancellationToken ct = default)
    {
        var service = await photoRepository.FindServiceAsync(serviceId, ct);
        if (service is not { IsActive: true, Business.IsActive: true })
            return null;

        var (items, total) = await photoRepository.ListServicePhotosAsync(serviceId, page, pageSize, ct);
        return ToPage(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<PhotoDto> UploadServicePhotoAsync(
        Guid serviceId, Guid callerId, PhotoUpload upload, CancellationToken ct = default)
    {
        await RequireOwnedServiceAsync(serviceId, callerId, ct);

        var maxPhotos = limits.Value.MaxPerService;
        var (_, total) = await photoRepository.ListServicePhotosAsync(serviceId, page: 1, pageSize: 1, ct);
        if (total >= maxPhotos)
            throw new PhotoLimitReachedException(maxPhotos);

        var photo = new ServicePhoto { ServiceId = serviceId, DisplayOrder = upload.DisplayOrder };
        await StoreAsync(upload.Content, key =>
        {
            photo.StorageKey = key;
            return photoRepository.AddServicePhotoAsync(photo, maxPhotos, ct);
        }, ct);

        logger.LogInformation("Photo {PhotoId} uploaded for service {ServiceId}.", photo.Id, serviceId);
        return ToDto(photo);
    }

    public async Task<bool> DeleteServicePhotoAsync(
        Guid serviceId, Guid photoId, Guid callerId, CancellationToken ct = default)
    {
        await RequireOwnedServiceAsync(serviceId, callerId, ct);

        var photo = await photoRepository.GetServicePhotoAsync(serviceId, photoId, ct);
        if (photo is null)
            return false;

        await photoRepository.DeleteServicePhotoAsync(photo, ct);
        await DeleteFileAsync(photo.StorageKey);

        logger.LogInformation("Photo {PhotoId} deleted from service {ServiceId}.", photo.Id, serviceId);
        return true;
    }

    // ---------- Files ----------

    public async Task<PhotoFile?> OpenPhotoFileAsync(string fileName, CancellationToken ct = default)
    {
        // Checked here as well as in the storage: only a well-formed key ever reaches the file system.
        if (!PhotoStorageKey.IsValid(fileName))
            return null;

        var content = await storage.OpenReadAsync(fileName, ct);
        // The content type comes from the key the server generated, never from the file's bytes.
        return content is null ? null : new PhotoFile(content, PhotoStorageKey.GetFormat(fileName).ContentType());
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Processes <paramref name="upload"/>, stores the result, and runs <paramref name="insertRow"/>
    /// with the new storage key. If the row can't be inserted (limit reached under the lock,
    /// database error, client gone), the stored file is deleted before the exception propagates.
    /// </summary>
    private async Task StoreAsync(Stream upload, Func<string, Task> insertRow, CancellationToken ct)
    {
        var processed = await imageProcessor.ProcessAsync(upload, ct);

        string key;
        using (var content = new MemoryStream(processed.Content, writable: false))
            key = await storage.SaveAsync(content, processed.Format, ct);

        try
        {
            await insertRow(key);
        }
        catch
        {
            await DeleteFileAsync(key);
            throw;
        }
    }

    /// <summary>
    /// Best-effort file removal, deliberately not tied to the request's cancellation: once the row
    /// is gone (or was never written), the file must go too, even if the client disconnected. A
    /// failure is logged instead of failing the request, since the row change already happened.
    /// </summary>
    private async Task DeleteFileAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // An orphaned file is still reachable by URL, so it needs a manual cleanup.
            logger.LogError(ex, "Could not delete photo file {StorageKey}; it is no longer referenced and must be removed manually.", key);
        }
    }

    private async Task RequireOwnedBusinessAsync(Guid businessId, Guid callerId, CancellationToken ct)
    {
        // #56's shared check: null → 404, BusinessAccessDeniedException → 403. An inactive business
        // still counts: its owner can keep managing it.
        if (await businessRepository.GetOwnedAsync(businessId, callerId, ct) is null)
            throw new BusinessNotFoundException(businessId);
    }

    /// <summary>Same rule as <see cref="RequireOwnedBusinessAsync"/>, through the service's business.</summary>
    private async Task<ServiceEntity> RequireOwnedServiceAsync(Guid serviceId, Guid callerId, CancellationToken ct)
    {
        var service = await photoRepository.FindServiceAsync(serviceId, ct)
            ?? throw new ServiceNotFoundException(serviceId);

        if (service.Business.AccountId != callerId)
            throw new BusinessAccessDeniedException(service.BusinessId, callerId);

        return service;
    }

    private PhotoDto ToDto(BusinessPhoto photo) => new(
        photo.Id, storage.GetPublicUrl(photo.StorageKey).ToString(), photo.IsPrimary, photo.DisplayOrder, photo.CreatedAt);

    private PhotoDto ToDto(ServicePhoto photo) => new(
        photo.Id, storage.GetPublicUrl(photo.StorageKey).ToString(), IsPrimary: null, photo.DisplayOrder, photo.CreatedAt);

    private static PaginatedResponse<PhotoDto> ToPage(IReadOnlyList<PhotoDto> items, int page, int pageSize, int total) =>
        new(items, new PaginationMeta(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
}
