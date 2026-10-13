using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, GET /business/media/photos/{fileName} (#65): serves the stored image
/// behind every <c>Photo.url</c>. Public: an &lt;img&gt; can't send a JWT.
/// </summary>
[ApiController]
[Route("business/media/photos")]
public class PhotoFilesController(IPhotoService photoService) : ControllerBase
{
    /// <summary>
    /// Stored files are immutable (every upload gets a new name), so they're cached for a year and
    /// the file name doubles as the ETag. Anything that isn't a well-formed, existing file name is
    /// a 404 — including traversal attempts, which never reach the file system.
    /// </summary>
    [HttpGet("{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhotoFile(string fileName, CancellationToken ct)
    {
        var file = await photoService.OpenPhotoFileAsync(fileName, ct);
        if (file is null)
            return NotFound(new ApiError("NOT_FOUND", "La foto indicada no existe."));

        var headers = Response.Headers;
        // The browser must treat the bytes as the declared image type and nothing else...
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        // ...and even if it didn't, nothing in it may run, load or be framed.
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; sandbox";
        // The web app and the mobile app load photos from other origins.
        headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";

        // FileStreamResult disposes the stream, and answers If-None-Match with 304 on its own.
        return File(file.Content, file.ContentType, lastModified: null,
            entityTag: new EntityTagHeaderValue($"\"{fileName}\""));
    }
}
