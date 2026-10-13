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
/// contracts/spot-api.yaml, "Business" tag, business photo endpoints (#65). Listing is public, so
/// [Authorize] is applied per-action. Writes require the BUSINESS account that owns the business.
/// </summary>
[ApiController]
[Route("business/businesses/{businessId:guid}/photos")]
public class BusinessPhotosController(IPhotoService photoService) : ControllerBase
{
    /// <summary>GET /business/businesses/{businessId}/photos: public. An inactive business is reported as 404.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<PhotoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListBusinessPhotos(
        Guid businessId, [FromQuery] PhotoListQuery query, CancellationToken ct)
    {
        if (!query.IsValid)
            return PhotoErrorResponses.InvalidPaging();

        var result = await photoService.ListBusinessPhotosAsync(businessId, query.Page, query.PageSize, ct);
        return result is null ? BusinessNotFound() : Ok(result);
    }

    /// <summary>
    /// POST /business/businesses/{businessId}/photos: multipart/form-data with <c>file</c>, and
    /// optional <c>isPrimary</c> and <c>displayOrder</c>. Requires the BUSINESS account that owns
    /// the business. The image is validated and re-encoded before it's stored.
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
    public async Task<IActionResult> UploadBusinessPhoto(Guid businessId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return PhotoErrorResponses.InvalidToken();

        var (upload, error) = await PhotoUploadForm.ReadAsync(Request, acceptsIsPrimary: true, ct);
        if (error is not null)
            return error;

        try
        {
            await using var content = upload!.File.OpenReadStream();
            var photo = await photoService.UploadBusinessPhotoAsync(
                businessId, callerId, new PhotoUpload(content, upload.DisplayOrder, upload.IsPrimary), ct);
            return StatusCode(StatusCodes.Status201Created, photo);
        }
        catch (BusinessNotFoundException)
        {
            return BusinessNotFound();
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
            return PhotoErrorResponses.LimitReached(ex, "El negocio");
        }
    }

    /// <summary>
    /// DELETE /business/businesses/{businessId}/photos/{photoId}: requires the BUSINESS account
    /// that owns the business. Hard delete, file included. A photo of another business is a 404.
    /// </summary>
    [HttpDelete("{photoId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBusinessPhoto(Guid businessId, Guid photoId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return PhotoErrorResponses.InvalidToken();

        try
        {
            var deleted = await photoService.DeleteBusinessPhotoAsync(businessId, photoId, callerId, ct);
            return deleted ? NoContent() : PhotoErrorResponses.PhotoNotFound();
        }
        catch (BusinessNotFoundException)
        {
            return BusinessNotFound();
        }
        catch (BusinessAccessDeniedException)
        {
            return PhotoErrorResponses.NotOwner();
        }
    }

    private NotFoundObjectResult BusinessNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name is the JWT sub.</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
