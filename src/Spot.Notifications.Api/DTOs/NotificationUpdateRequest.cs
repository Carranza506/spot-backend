using System.ComponentModel.DataAnnotations;

namespace Spot.Notifications.Api.DTOs;

/// <summary>
/// Matches the NotificationUpdateRequest schema in contracts/spot-api.yaml. Nullable only so
/// [Required] can reject a missing isRead instead of binding it as false.
/// </summary>
public sealed class NotificationUpdateRequest
{
    [Required]
    public bool? IsRead { get; set; }
}
