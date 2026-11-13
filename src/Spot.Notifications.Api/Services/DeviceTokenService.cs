using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;

namespace Spot.Notifications.Api.Services;

public sealed class DeviceTokenService(IDeviceTokenRepository repository) : IDeviceTokenService
{
    public async Task<DeviceTokenDto> RegisterAsync(
        Guid userId, string token, DevicePlatform platform, CancellationToken ct = default)
    {
        var deviceToken = await repository.UpsertAsync(userId, token, platform, ct);
        return DeviceTokenDto.FromEntity(deviceToken);
    }

    public async Task<DeviceTokenRemovalResult> RemoveAsync(Guid userId, Guid deviceTokenId, CancellationToken ct = default)
    {
        var token = await repository.GetByIdAsync(deviceTokenId, ct);
        if (token is null)
            return DeviceTokenRemovalResult.NotFound;

        if (token.UserId != userId)
            return DeviceTokenRemovalResult.Forbidden;

        await repository.DeactivateAsync(token, ct);
        return DeviceTokenRemovalResult.Removed;
    }
}
