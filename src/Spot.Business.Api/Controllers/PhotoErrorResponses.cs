using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.Photos;
using Spot.Business.Api.Repositories;
using Spot.Shared.Errors;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// Error responses shared by the business and service photo endpoints, in the contract's Error
/// shape. Messages are user-facing (Spanish) and never echo the upload or internal details.
/// </summary>
internal static class PhotoErrorResponses
{
    public static IActionResult InvalidPhoto(InvalidPhotoException ex) => ex.Reason switch
    {
        InvalidPhotoReason.TooLarge => PhotoUploadForm.PayloadTooLarge(),
        InvalidPhotoReason.TooManyPixels => BadRequest("La foto no puede superar los 40 megapíxeles."),
        InvalidPhotoReason.Corrupt => BadRequest("La foto está dañada o incompleta."),
        _ => BadRequest("La foto debe ser JPG, PNG o WebP."),
    };

    public static ObjectResult LimitReached(PhotoLimitReachedException ex, string owner) =>
        new(new ApiError("PHOTO_LIMIT_REACHED", $"{owner} ya tiene el máximo de {ex.MaxPhotos} fotos."))
        {
            StatusCode = StatusCodes.Status409Conflict,
        };

    public static NotFoundObjectResult PhotoNotFound() =>
        new(new ApiError("NOT_FOUND", "La foto indicada no existe."));

    public static ObjectResult NotOwner() =>
        new(new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."))
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };

    public static UnauthorizedObjectResult InvalidToken() =>
        new(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    public static BadRequestObjectResult InvalidPaging() =>
        new(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

    private static BadRequestObjectResult BadRequest(string message) =>
        new(new ApiError("BAD_REQUEST", message, new { field = "file" }));
}
