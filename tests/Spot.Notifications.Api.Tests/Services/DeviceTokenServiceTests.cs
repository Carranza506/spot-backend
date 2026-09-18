using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Services;
using Spot.Notifications.Api.Tests.Fakes;

namespace Spot.Notifications.Api.Tests.Services;

public class DeviceTokenServiceTests
{
    [Fact]
    public async Task RegisterAsync_NewToken_ReturnsDtoWithoutTokenOrUserId()
    {
        var repo = new FakeDeviceTokenRepository();
        var service = new DeviceTokenService(repo);
        var userId = Guid.NewGuid();

        var result = await service.RegisterAsync(userId, "fcm-token-abc123", DevicePlatform.ANDROID);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("ANDROID", result.Platform);
    }

    [Fact]
    public async Task RegisterAsync_ExistingInactiveToken_ReactivatesInsteadOfDuplicating()
    {
        var repo = new FakeDeviceTokenRepository();
        var userId = Guid.NewGuid();
        var existing = repo.Seed(new DeviceToken
        {
            UserId = userId,
            Token = "fcm-token-abc123",
            Platform = DevicePlatform.ANDROID,
            IsActive = false,
        });
        var service = new DeviceTokenService(repo);

        var result = await service.RegisterAsync(userId, "fcm-token-abc123", DevicePlatform.IOS);

        Assert.Equal(existing.Id, result.Id);
        Assert.True(existing.IsActive);
        Assert.Equal(DevicePlatform.IOS, existing.Platform);
    }

    [Fact]
    public async Task RegisterAsync_ExistingActiveToken_IsIdempotent()
    {
        var repo = new FakeDeviceTokenRepository();
        var userId = Guid.NewGuid();
        var existing = repo.Seed(new DeviceToken
        {
            UserId = userId,
            Token = "fcm-token-abc123",
            Platform = DevicePlatform.ANDROID,
            IsActive = true,
        });
        var service = new DeviceTokenService(repo);

        var result = await service.RegisterAsync(userId, "fcm-token-abc123", DevicePlatform.ANDROID);

        Assert.Equal(existing.Id, result.Id);
    }

    [Fact]
    public async Task RegisterAsync_TokenOwnedByAnotherUser_ReassignsToTheRegisteringUser()
    {
        var repo = new FakeDeviceTokenRepository();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var existing = repo.Seed(new DeviceToken
        {
            UserId = userA,
            Token = "shared-device-token",
            Platform = DevicePlatform.ANDROID,
            IsActive = true,
        });
        var service = new DeviceTokenService(repo);

        var result = await service.RegisterAsync(userB, "shared-device-token", DevicePlatform.IOS);

        Assert.Equal(existing.Id, result.Id);
        Assert.Equal(userB, existing.UserId);
        Assert.Equal(DevicePlatform.IOS, existing.Platform);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task RegisterAsync_SoftDeletedTokenClaimedByDifferentUser_ReactivatesAndReassignsIt()
    {
        var repo = new FakeDeviceTokenRepository();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var existing = repo.Seed(new DeviceToken
        {
            UserId = userA,
            Token = "removed-device-token",
            Platform = DevicePlatform.ANDROID,
            IsActive = false,
        });
        var service = new DeviceTokenService(repo);

        var result = await service.RegisterAsync(userB, "removed-device-token", DevicePlatform.WEB);

        Assert.Equal(existing.Id, result.Id);
        Assert.Equal(userB, existing.UserId);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task RemoveAsync_AfterReassignmentToAnotherUser_OriginalOwnerIsForbidden()
    {
        var repo = new FakeDeviceTokenRepository();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var existing = repo.Seed(new DeviceToken
        {
            UserId = userA,
            Token = "shared-device-token",
            Platform = DevicePlatform.ANDROID,
            IsActive = true,
        });
        var service = new DeviceTokenService(repo);
        await service.RegisterAsync(userB, "shared-device-token", DevicePlatform.ANDROID);

        var result = await service.RemoveAsync(userA, existing.Id);

        Assert.Equal(DeviceTokenRemovalResult.Forbidden, result);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task RemoveAsync_OwnToken_ReturnsRemoved_AndDeactivatesIt()
    {
        var repo = new FakeDeviceTokenRepository();
        var userId = Guid.NewGuid();
        var token = repo.Seed(new DeviceToken { UserId = userId, Token = "t", Platform = DevicePlatform.WEB });
        var service = new DeviceTokenService(repo);

        var result = await service.RemoveAsync(userId, token.Id);

        Assert.Equal(DeviceTokenRemovalResult.Removed, result);
        Assert.False(token.IsActive);
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_ReturnsNotFound()
    {
        var repo = new FakeDeviceTokenRepository();
        var service = new DeviceTokenService(repo);

        var result = await service.RemoveAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(DeviceTokenRemovalResult.NotFound, result);
    }

    [Fact]
    public async Task RemoveAsync_TokenOwnedByAnotherUser_ReturnsForbidden_AndDoesNotDeactivateIt()
    {
        var repo = new FakeDeviceTokenRepository();
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var token = repo.Seed(new DeviceToken { UserId = owner, Token = "t", Platform = DevicePlatform.WEB });
        var service = new DeviceTokenService(repo);

        var result = await service.RemoveAsync(attacker, token.Id);

        Assert.Equal(DeviceTokenRemovalResult.Forbidden, result);
        Assert.True(token.IsActive);
    }
}
