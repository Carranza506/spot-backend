using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.DTOs;

/// <summary>Matches the "Notification" schema in contracts/spot-api.yaml.</summary>
public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    string Type,
    string Title,
    string Body,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    bool IsRead,
    DateTimeOffset CreatedAt)
{
    public static NotificationDto FromEntity(Notification notification) => new(
        Id: notification.Id,
        UserId: notification.UserId,
        Type: notification.Type.ToString(),
        Title: notification.Title,
        Body: notification.Body,
        RelatedEntityType: notification.RelatedEntityType,
        RelatedEntityId: notification.RelatedEntityId,
        IsRead: notification.IsRead,
        CreatedAt: notification.CreatedAt);
}
