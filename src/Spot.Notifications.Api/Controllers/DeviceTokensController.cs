using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Notifications.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Notifications" tag: <c>POST /notifications/device-tokens</c> and
/// <c>DELETE /notifications/device-tokens/{deviceTokenId}</c> (#77). Every action here requires
/// an authenticated caller (unlike AuthController, nothing under this route is public), so
/// <see cref="AuthorizeAttribute"/> is applied once at the class level.
/// </summary>
[ApiController]
[Route("notifications/device-tokens")]
[Authorize]
public class DeviceTokensController(IDeviceTokenService deviceTokenService) : ControllerBase
{
    /// <summary>
    /// POST /notifications/device-tokens: registers (or reactivates) the caller's device push
    /// token. The owning user always comes from the access token's `sub` claim, never the body.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DeviceTokenDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterDeviceToken([FromBody] DeviceTokenRegisterRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        if (!Enum.TryParse<DevicePlatform>(request.Platform, ignoreCase: true, out var platform))
        {
            return BadRequest(new ApiError(
                "BAD_REQUEST", $"platform debe ser uno de: {string.Join(", ", Enum.GetNames<DevicePlatform>())}."));
        }

        var result = await deviceTokenService.RegisterAsync(userId, request.Token.Trim(), platform, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// DELETE /notifications/device-tokens/{deviceTokenId}: removes a device push token. Only the
    /// token's own owner may remove it — someone else's token id answers 403, an unknown one 404.
    /// </summary>
    [HttpDelete("{deviceTokenId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveDeviceToken(Guid deviceTokenId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        var result = await deviceTokenService.RemoveAsync(userId, deviceTokenId, ct);

        return result switch
        {
            DeviceTokenRemovalResult.Removed => NoContent(),
            DeviceTokenRemovalResult.NotFound => NotFound(new ApiError("NOT_FOUND", "El token indicado no existe.")),
            DeviceTokenRemovalResult.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                new ApiError("FORBIDDEN", "No tienes permiso para eliminar este token.")),
            _ => throw new InvalidOperationException($"Unhandled {nameof(DeviceTokenRemovalResult)}: {result}"),
        };
    }

    /// <summary>
    /// The access token itself (signature, issuer, audience, lifetime) is already validated by
    /// <see cref="AuthorizeAttribute"/> before any action here runs. AddSpotJwtAuthentication
    /// configures NameClaimType = "sub", so User.Identity.Name IS the caller's user id — this
    /// only guards against that claim somehow not being a Guid, which a token actually issued by
    /// Spot.Auth.Api never produces. Same pattern as Spot.Auth.Api's AuthController.
    /// </summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
