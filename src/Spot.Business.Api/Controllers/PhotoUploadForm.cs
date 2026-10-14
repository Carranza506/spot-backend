using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Spot.Shared.Errors;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// Reads and validates the multipart/form-data body of the photo upload endpoints (contract:
/// <c>file</c>, optional <c>displayOrder</c>, and <c>isPrimary</c> for business photos only).
/// </summary>
/// <remarks>
/// The form is read by hand rather than bound with [FromForm] IFormFile, so every size, shape and
/// field error is answered with the contract's Error body (400/413) instead of surfacing as an
/// exception through the global 500 handler. The limits below are enforced while reading, before
/// the whole body is buffered: the controllers apply <see cref="MaxRequestBytes"/> with
/// [RequestSizeLimit] and the form limits with [RequestFormLimits].
/// </remarks>
internal static class PhotoUploadForm
{
    /// <summary>Contract: photos up to 5 MB. Same as PhotoProcessing:MaxBytes' default.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>The file plus room for the multipart boundaries, part headers and the two small fields.</summary>
    public const long MaxRequestBytes = MaxFileBytes + 64 * 1024;

    // Form limits for [RequestFormLimits]: a handful of short fields, nothing else.
    public const int MaxFormValues = 8;
    public const int MaxKeyLength = 64;
    public const int MaxValueLength = 64;

    private const string FileField = "file";
    private const string DisplayOrderField = "displayOrder";
    private const string IsPrimaryField = "isPrimary";

    public sealed record Result(IFormFile File, int DisplayOrder, bool IsPrimary);

    /// <summary>
    /// The validated upload, or the error response to return instead (<paramref name="error"/>).
    /// </summary>
    public static async Task<(Result? Upload, IActionResult? Error)> ReadAsync(
        HttpRequest request, bool acceptsIsPrimary, CancellationToken ct)
    {
        if (!request.HasFormContentType
            || !request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return (null, BadRequest("La foto debe enviarse como multipart/form-data.", FileField));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Body larger than MaxRequestBytes: Kestrel stops reading it right at the limit.
            return (null, PayloadTooLarge());
        }
        catch (InvalidDataException)
        {
            // MultipartBodyLengthLimit: same 413 as above, for hosts that don't apply [RequestSizeLimit].
            if (request.ContentLength > MaxRequestBytes)
                return (null, PayloadTooLarge());

            // Malformed multipart, or too many/too long fields ([RequestFormLimits]).
            return (null, BadRequest("El formulario de la foto no es válido.", FileField));
        }
        catch (IOException)
        {
            // A body that ends mid-stream (garbage after the boundary, an unterminated part, truncated
            // part headers): the multipart reader throws "Unexpected end of Stream". Also covers any
            // other BadHttpRequestException (it derives from IOException), since the 413 one is
            // caught above.
            return (null, BadRequest("El formulario de la foto no es válido.", FileField));
        }

        if (form.Files.Count == 0)
            return (null, BadRequest("La foto es requerida.", FileField));
        if (form.Files.Count > 1 || form.Files[0].Name != FileField)
            return (null, BadRequest("Envía una sola foto, en el campo 'file'.", FileField));

        var file = form.Files[0];
        if (file.Length == 0)
            return (null, BadRequest("La foto está vacía.", FileField));
        if (file.Length > MaxFileBytes)
            return (null, PayloadTooLarge());

        var displayOrder = 0;
        if (form.TryGetValue(DisplayOrderField, out var displayOrderValues))
        {
            if (displayOrderValues.Count != 1
                || !int.TryParse(displayOrderValues[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out displayOrder)
                || displayOrder < 0)
            {
                return (null, BadRequest("displayOrder debe ser un número entero mayor o igual a 0.", DisplayOrderField));
            }
        }

        var isPrimary = false;
        // Not part of the service photo contract: ignored there, like any other unknown field.
        if (acceptsIsPrimary && form.TryGetValue(IsPrimaryField, out var isPrimaryValues))
        {
            if (isPrimaryValues.Count != 1 || !bool.TryParse(isPrimaryValues[0], out isPrimary))
                return (null, BadRequest("isPrimary debe ser true o false.", IsPrimaryField));
        }

        return (new Result(file, displayOrder, isPrimary), null);
    }

    public static ObjectResult PayloadTooLarge() => new(new ApiError("PAYLOAD_TOO_LARGE", "La foto no puede pesar más de 5 MB."))
    {
        StatusCode = StatusCodes.Status413PayloadTooLarge,
    };

    private static BadRequestObjectResult BadRequest(string message, string field) =>
        new(new ApiError("BAD_REQUEST", message, new { field }));
}
