using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Tests.Controllers;

public class DeviceTokensControllerTests(NotificationsApiFactory factory) : IClassFixture<NotificationsApiFactory>
{
    [Fact]
    public async Task RegisterDeviceToken_ValidRequest_Returns201WithDeviceToken()
    {
        factory.DeviceTokenRepository.Reset();
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "fcm-token-abc123", platform = "ANDROID" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("id", out _));
        Assert.Equal("ANDROID", body.GetProperty("platform").GetString());
        Assert.True(body.TryGetProperty("createdAt", out _));
        // The raw token and the owning user id are never echoed back.
        Assert.False(body.TryGetProperty("token", out _));
        Assert.False(body.TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task RegisterDeviceToken_NoAccessToken_Returns401()
    {
        factory.DeviceTokenRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "fcm-token-abc123", platform = "ANDROID" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDeviceToken_MissingToken_Returns400WithErrorShape()
    {
        factory.DeviceTokenRepository.Reset();
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/notifications/device-tokens", new { platform = "ANDROID" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task RegisterDeviceToken_InvalidPlatform_Returns400WithErrorShape()
    {
        factory.DeviceTokenRepository.Reset();
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "fcm-token-abc123", platform = "BLACKBERRY" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RegisterDeviceToken_AlreadyRegisteredBySameUser_ReactivatesInsteadOfDuplicating()
    {
        factory.DeviceTokenRepository.Reset();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var first = await client.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "fcm-token-abc123", platform = "ANDROID" });
        var second = await client.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "fcm-token-abc123", platform = "ANDROID" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        // Same underlying row reactivated, not a second one created.
        Assert.Equal(firstBody.GetProperty("id").GetGuid(), secondBody.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task RegisterDeviceToken_OwnedByAnotherUser_ReassignsToTheRegisteringUser_AndOriginalOwnerLosesAccess()
    {
        factory.DeviceTokenRepository.Reset();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var clientA = CreateAuthenticatedClient(userA);
        var clientB = CreateAuthenticatedClient(userB);

        var registeredByA = await clientA.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "shared-device-token", platform = "ANDROID" });
        var registeredByB = await clientB.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "shared-device-token", platform = "IOS" });

        Assert.Equal(HttpStatusCode.Created, registeredByA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, registeredByB.StatusCode);

        var idByA = (await registeredByA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var idByB = (await registeredByB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Same row reassigned to B, not a second one created for B alongside A's.
        Assert.Equal(idByA, idByB);

        // A no longer owns it: their own DELETE on that id is now forbidden.
        var deleteByA = await clientA.DeleteAsync($"/notifications/device-tokens/{idByA}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteByA.StatusCode);

        // B does own it now.
        var deleteByB = await clientB.DeleteAsync($"/notifications/device-tokens/{idByB}");
        Assert.Equal(HttpStatusCode.NoContent, deleteByB.StatusCode);
    }

    [Fact]
    public async Task RegisterDeviceToken_SoftDeletedTokenRegisteredByAnotherUser_ReactivatesAndReassignsIt()
    {
        factory.DeviceTokenRepository.Reset();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var removedToken = factory.DeviceTokenRepository.Seed(new DeviceToken
        {
            UserId = userA,
            Token = "previously-removed-token",
            Platform = DevicePlatform.ANDROID,
            IsActive = false,
        });
        var clientB = CreateAuthenticatedClient(userB);

        var response = await clientB.PostAsJsonAsync(
            "/notifications/device-tokens", new { token = "previously-removed-token", platform = "WEB" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(removedToken.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(userB, removedToken.UserId);
        Assert.True(removedToken.IsActive);
    }

    [Fact]
    public async Task RemoveDeviceToken_OwnToken_Returns204()
    {
        factory.DeviceTokenRepository.Reset();
        var userId = Guid.NewGuid();
        var token = factory.DeviceTokenRepository.Seed(
            new DeviceToken { UserId = userId, Token = "fcm-token-abc123", Platform = DevicePlatform.ANDROID });
        var client = CreateAuthenticatedClient(userId);

        var response = await client.DeleteAsync($"/notifications/device-tokens/{token.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveDeviceToken_NoAccessToken_Returns401()
    {
        factory.DeviceTokenRepository.Reset();
        var token = factory.DeviceTokenRepository.Seed(
            new DeviceToken { UserId = Guid.NewGuid(), Token = "fcm-token-abc123", Platform = DevicePlatform.ANDROID });
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/notifications/device-tokens/{token.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RemoveDeviceToken_UnknownId_Returns404WithErrorShape()
    {
        factory.DeviceTokenRepository.Reset();
        var client = CreateAuthenticatedClient();

        var response = await client.DeleteAsync($"/notifications/device-tokens/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RemoveDeviceToken_OwnedByAnotherUser_Returns403WithErrorShape()
    {
        factory.DeviceTokenRepository.Reset();
        var owner = Guid.NewGuid();
        var token = factory.DeviceTokenRepository.Seed(
            new DeviceToken { UserId = owner, Token = "fcm-token-abc123", Platform = DevicePlatform.ANDROID });
        // A different, but validly authenticated, caller.
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync($"/notifications/device-tokens/{token.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
    }

    private HttpClient CreateAuthenticatedClient(Guid? userId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId ?? Guid.NewGuid()));
        return client;
    }
}
