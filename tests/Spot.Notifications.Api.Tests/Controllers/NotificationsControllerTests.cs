using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Tests.Controllers;

[Collection(NotificationsApiCollection.Name)]
public class NotificationsControllerTests(NotificationsApiFactory factory) : IClassFixture<NotificationsApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // ---------- GET /notifications ----------

    [Fact]
    public async Task ListOwnNotifications_ReturnsOnlyTheCallersNotifications_NewestFirst_InContractShape()
    {
        factory.NotificationRepository.Reset();
        var userId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var newest = Seed(userId, T0.AddMinutes(1), type: NotificationType.BOOKING_STATUS_CHANGED,
            relatedEntityType: "booking", relatedEntityId: relatedId);
        var older = Seed(userId, T0);
        Seed(Guid.NewGuid(), T0.AddMinutes(5));

        var response = await CreateAuthenticatedClient(userId).GetAsync("/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal([newest.Id, older.Id], data.Select(x => x.GetProperty("id").GetGuid()));

        // Exactly the contract's Notification properties, with the enum as its string label.
        var first = data[0];
        Assert.Equal(
            ["id", "userId", "type", "title", "body", "relatedEntityType", "relatedEntityId", "isRead", "createdAt"],
            first.EnumerateObject().Select(p => p.Name));
        Assert.Equal(userId, first.GetProperty("userId").GetGuid());
        Assert.Equal("BOOKING_STATUS_CHANGED", first.GetProperty("type").GetString());
        Assert.Equal("booking", first.GetProperty("relatedEntityType").GetString());
        Assert.Equal(relatedId, first.GetProperty("relatedEntityId").GetGuid());
        Assert.False(first.GetProperty("isRead").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data[1].GetProperty("relatedEntityType").ValueKind);

        var pagination = body.GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(20, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, pagination.GetProperty("total").GetInt32());
        Assert.Equal(1, pagination.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("true", 1)]
    [InlineData("false", 2)]
    public async Task ListOwnNotifications_IsReadFilter_ReturnsOnlyMatchingRows(string isRead, int expected)
    {
        factory.NotificationRepository.Reset();
        var userId = Guid.NewGuid();
        Seed(userId, T0, isRead: true);
        Seed(userId, T0.AddMinutes(1), isRead: false);
        Seed(userId, T0.AddMinutes(2), isRead: false);

        var response = await CreateAuthenticatedClient(userId).GetAsync($"/notifications?isRead={isRead}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(expected, data.Count);
        Assert.All(data, x => Assert.Equal(bool.Parse(isRead), x.GetProperty("isRead").GetBoolean()));
        Assert.Equal(expected, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListOwnNotifications_Paginates()
    {
        factory.NotificationRepository.Reset();
        var userId = Guid.NewGuid();
        var seeded = Enumerable.Range(0, 3).Select(i => Seed(userId, T0.AddMinutes(i))).ToList();
        var client = CreateAuthenticatedClient(userId);

        var page2 = await client.GetFromJsonAsync<JsonElement>("/notifications?page=2&pageSize=2");

        var data = page2.GetProperty("data").EnumerateArray().ToList();
        // Newest first: page 1 holds minutes 2 and 1, page 2 the oldest.
        Assert.Equal(seeded[0].Id, Assert.Single(data).GetProperty("id").GetGuid());
        var pagination = page2.GetProperty("pagination");
        Assert.Equal(2, pagination.GetProperty("page").GetInt32());
        Assert.Equal(2, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, pagination.GetProperty("total").GetInt32());
        Assert.Equal(2, pagination.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?isRead=abc")]
    [InlineData("?page=abc")]
    public async Task ListOwnNotifications_InvalidQuery_Returns400WithErrorShape(string query)
    {
        var response = await CreateAuthenticatedClient(Guid.NewGuid()).GetAsync($"/notifications{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task ListOwnNotifications_NoAccessToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- PATCH /notifications/{notificationId} ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MarkNotificationRead_Owner_Returns200WithTheUpdatedNotification(bool isRead)
    {
        factory.NotificationRepository.Reset();
        var userId = Guid.NewGuid();
        var notification = Seed(userId, T0, isRead: !isRead);

        var response = await CreateAuthenticatedClient(userId).PatchAsJsonAsync(
            $"/notifications/{notification.Id}", new { isRead });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(notification.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(isRead, body.GetProperty("isRead").GetBoolean());
        Assert.Equal("GENERAL", body.GetProperty("type").GetString());
        Assert.Equal(isRead, notification.IsRead);
    }

    [Fact]
    public async Task MarkNotificationRead_SomeoneElsesNotification_Returns403AndLeavesItUnchanged()
    {
        factory.NotificationRepository.Reset();
        var notification = Seed(Guid.NewGuid(), T0, isRead: false);

        var response = await CreateAuthenticatedClient(Guid.NewGuid()).PatchAsJsonAsync(
            $"/notifications/{notification.Id}", new { isRead = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task MarkNotificationRead_UnknownId_Returns404()
    {
        factory.NotificationRepository.Reset();

        var response = await CreateAuthenticatedClient(Guid.NewGuid()).PatchAsJsonAsync(
            $"/notifications/{Guid.NewGuid()}", new { isRead = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "isRead": null }""")]
    [InlineData("""{ "isRead": "yes" }""")]
    public async Task MarkNotificationRead_InvalidBody_Returns400AndLeavesItUnchanged(string json)
    {
        factory.NotificationRepository.Reset();
        var userId = Guid.NewGuid();
        var notification = Seed(userId, T0, isRead: false);

        var response = await CreateAuthenticatedClient(userId).PatchAsync(
            $"/notifications/{notification.Id}", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task MarkNotificationRead_NoAccessToken_Returns401()
    {
        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/notifications/{Guid.NewGuid()}", new { isRead = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Notification Seed(
        Guid userId, DateTimeOffset createdAt, bool isRead = false, NotificationType type = NotificationType.GENERAL,
        string? relatedEntityType = null, Guid? relatedEntityId = null) =>
        factory.NotificationRepository.Seed(new Notification
        {
            UserId = userId,
            Type = type,
            Title = "Título",
            Body = "Cuerpo",
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            IsRead = isRead,
            CreatedAt = createdAt,
        });

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId));
        return client;
    }
}
