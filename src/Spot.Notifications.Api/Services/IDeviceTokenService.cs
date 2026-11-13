using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Services;

public interface IDeviceTokenService
{
    /// <summary>
    /// Registers <paramref name="token"/> for <paramref name="userId"/>. If the token is already
    /// registered — to this user or another one, active or previously removed — it's reassigned
    /// to <paramref name="userId"/> and reactivated rather than duplicated.
    /// </summary>
    Task<DeviceTokenDto> RegisterAsync(Guid userId, string token, DevicePlatform platform, CancellationToken ct = default);

    /// <summary>
    /// Removes <paramref name="deviceTokenId"/> on behalf of <paramref name="userId"/>. See
    /// <see cref="DeviceTokenRemovalResult"/> for the possible outcomes.
    /// </summary>
    Task<DeviceTokenRemovalResult> RemoveAsync(Guid userId, Guid deviceTokenId, CancellationToken ct = default);
}

public enum DeviceTokenRemovalResult
{
    Removed,
    NotFound,

    /// <summary>The token exists but is owned by a different user.</summary>
    Forbidden,
}
