using Microsoft.AspNetCore.Mvc;
using Spot.Notifications.Api.Auth;
using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Notifications.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Notifications" tag: service-to-service endpoints, authenticated with
/// the internal API key instead of a user JWT (no <c>[Authorize]</c> here on purpose). The Gateway
/// only routes them on its internal port.
/// </summary>
[ApiController]
[Route("notifications/internal")]
[TypeFilter<InternalApiKeyFilter>]
public class InternalNotificationsController(INotificationService notificationService) : ControllerBase
{
    /// <summary>
    /// POST /notifications/internal/booking-status-changed (operationId
    /// triggerBookingStatusNotification, #79): stores and pushes the notification for a booking's
    /// new status. A failed push doesn't change the 202.
    /// </summary>
    [HttpPost("booking-status-changed")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerBookingStatusNotification(
        [FromBody] InternalBookingStatusNotificationRequest request, CancellationToken ct)
    {
        if (request.Status == BookingStatus.PENDING)
            return BadRequest(new ApiError("BAD_REQUEST", "status no puede ser PENDING: no es un cambio de estado.", new { field = "status" }));

        var result = await notificationService.NotifyBookingStatusChangedAsync(request, ct);

        return result switch
        {
            BookingStatusNotificationResult.Accepted => Accepted(),
            BookingStatusNotificationResult.UserNotFound => NotFound(new ApiError("NOT_FOUND", "El usuario indicado no existe.")),
            BookingStatusNotificationResult.BusinessNotFound => NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe.")),
            _ => throw new InvalidOperationException($"Unhandled {nameof(BookingStatusNotificationResult)}: {result}"),
        };
    }
}
