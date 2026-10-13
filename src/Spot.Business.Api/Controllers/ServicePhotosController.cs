using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Photos;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, service photo endpoints (#65). A separate controller
/// from ServicesController, so photos don't overlap with the service CRUD stories (#114-#116).
/// Listing is public; writes require the BUSINESS account that owns the service's business.
/// </summary>
[ApiController]
[Route("business/services/{serviceId:guid}/photos")]
public class ServicePhotosController(IPhotoService photoService) : ControllerBase
{
    /// <summary>
    /// GET /business/services/{serviceId}/photos: public. An inactive service, or a service of an
    /// inactive business, is reported as 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<PhotoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListServicePhotos(
        Guid serviceId, [FromQuery] PhotoListQuery query, CancellationToken ct)
    {
        if (!query.IsValid)
            return PhotoErrorResponses.InvalidPaging();

        var result = await photoService.ListServicePhotosAsync(serviceId, query.Page, query.PageSize, ct);
        return result is null ? ServiceNotFound() : Ok(result);
    }

    /// <summary>
    /// POST /business/services/{serviceId}/photos: multipart/form-data with <c>file</c> and an
    /// optional <c>displayOrder</c>. Requires the BUSINESS account that owns the service's business.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "BUSINESS")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(PhotoUploadForm.MaxRequestBytes)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = PhotoUploadForm.MaxRequestBytes,
        ValueCountLimit = PhotoUploadForm.MaxFormValues,
        KeyLengthLimit = PhotoUploadForm.MaxKeyLength,
        ValueLengthLimit = PhotoUploadForm.MaxValueLength)]
    [ProducesResponseType(typeof(PhotoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadServicePhoto(Guid serviceId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return PhotoErrorResponses.InvalidToken();

        var (upload, error) = await PhotoUploadForm.ReadAsync(Request, acceptsIsPrimary: false, ct);
        if (error is not null)
            return error;

        try
        {
            await using var content = upload!.File.OpenReadStream();
            var photo = await photoService.UploadServicePhotoAsync(
                serviceId, callerId, new PhotoUpload(content, upload.DisplayOrder), ct);
            return StatusCode(StatusCodes.Status201Created, photo);
        }
        catch (ServiceNotFoundException)
        {
            return ServiceNotFound();
        }
        catch (BusinessAccessDeniedException)
        {
            return PhotoErrorResponses.NotOwner();
        }
        catch (InvalidPhotoException ex)
        {
            return PhotoErrorResponses.InvalidPhoto(ex);
        }
        catch (PhotoLimitReachedException ex)
        {
            return PhotoErrorResponses.LimitReached(ex, "El servicio");
        }
    }

    /// <summary>
    /// DELETE /business/services/{serviceId}/photos/{photoId}: requires the BUSINESS account that
    /// owns the service's business. Hard delete, file included. A photo of another service is a 404.
    /// </summary>
    [HttpDelete("{photoId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteServicePhoto(Guid serviceId, Guid photoId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return PhotoErrorResponses.InvalidToken();

        try
        {
            var deleted = await photoService.DeleteServicePhotoAsync(serviceId, photoId, callerId, ct);
            return deleted ? NoContent() : PhotoErrorResponses.PhotoNotFound();
        }
        catch (ServiceNotFoundException)
        {
            return ServiceNotFound();
        }
        catch (BusinessAccessDeniedException)
        {
            return PhotoErrorResponses.NotOwner();
        }
    }

    private NotFoundObjectResult ServiceNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "El servicio indicado no existe."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name is the JWT sub.</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
