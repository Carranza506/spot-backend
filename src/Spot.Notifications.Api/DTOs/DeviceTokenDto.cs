using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.DTOs;

/// <summary>
/// Matches the "DeviceToken" schema in contracts/spot-api.yaml. Never carries the raw token
/// string or the owning user id back to the client — the caller already knows both.
/// </summary>
public sealed record DeviceTokenDto(Guid Id, string Platform, DateTimeOffset CreatedAt)
{
    public static DeviceTokenDto FromEntity(DeviceToken token) => new(
        Id: token.Id,
        Platform: token.Platform.ToString(),
        CreatedAt: token.CreatedAt);
}
