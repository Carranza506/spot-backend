using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Notifications.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Notifications" tag: <c>GET /notifications</c> and
/// <c>PATCH /notifications/{notificationId}</c> (#78). Any authenticated user, over their own
/// notifications only, so <see cref="AuthorizeAttribute"/> is applied once at the class level.
/// </summary>
[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController(INotificationService notificationService) : ControllerBase
{
    /// <summary>GET /notifications: the caller's notifications, newest first, optionally filtered by isRead.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListOwnNotifications(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool? isRead = null,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        return Ok(await notificationService.ListAsync(userId, isRead, page, pageSize, ct));
    }

    /// <summary>
    /// PATCH /notifications/{notificationId}: sets isRead. Only the notification's own user may
    /// change it — someone else's id answers 403, an unknown one 404 (same as RemoveDeviceToken).
    /// </summary>
    [HttpPatch("{notificationId}")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkNotificationRead(
        Guid notificationId, [FromBody] NotificationUpdateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        var result = await notificationService.SetReadAsync(userId, notificationId, request.IsRead!.Value, ct);

        return result.Outcome switch
        {
            NotificationUpdateOutcome.Updated => Ok(result.Notification),
            NotificationUpdateOutcome.NotFound => NotFound(new ApiError("NOT_FOUND", "La notificación indicada no existe.")),
            NotificationUpdateOutcome.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                new ApiError("FORBIDDEN", "No tienes permiso para modificar esta notificación.")),
            _ => throw new InvalidOperationException($"Unhandled {nameof(NotificationUpdateOutcome)}: {result.Outcome}"),
        };
    }

    /// <summary>Same as DeviceTokensController.TryGetUserId: User.Identity.Name IS the caller's user id (JWT sub).</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
