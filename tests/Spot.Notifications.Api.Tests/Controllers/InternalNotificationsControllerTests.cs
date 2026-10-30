using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Services.Push;

namespace Spot.Notifications.Api.Tests.Controllers;

/// <summary>POST /notifications/internal/booking-status-changed (#79) through the real pipeline.</summary>
[Collection(NotificationsApiCollection.Name)]
public class InternalNotificationsControllerTests : IClassFixture<NotificationsApiFactory>
{
    private const string Url = "/notifications/internal/booking-status-changed";

    private readonly NotificationsApiFactory _factory;
    private readonly Guid _bookingId = Guid.NewGuid();
    private readonly Guid _businessId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _businessAccountId = Guid.NewGuid();

    public InternalNotificationsControllerTests(NotificationsApiFactory factory)
    {
        _factory = factory;
        factory.NotificationRepository.Reset();
        factory.PushNotifications.Reset();
        factory.NotificationRepository.Users.Add(_clientId);
        factory.NotificationRepository.Users.Add(_businessAccountId);
        factory.NotificationRepository.BusinessAccounts[_businessId] = _businessAccountId;
    }

    // ---------- 401 ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData(NotificationsApiFactory.InternalApiKey + "x")]
    public async Task WithoutTheRightKey_Returns401InTheErrorShape_AndNothingHappens(string? key)
    {
        var response = await PostAsync(Body("CANCELLED"), key);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        Assert.Empty(_factory.NotificationRepository.All);
        Assert.Empty(_factory.PushNotifications.Sent);
    }

    [Fact]
    public async Task WithoutTheKey_AnInvalidBodyIsStill401()
    {
        var response = await PostAsync("not json", key: null);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task AValidUserJwtIsNotEnough()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.IssueAccessToken(_clientId));

        var response = await client.PostAsync(Url, new StringContent(Body("CONFIRMED"), Encoding.UTF8, "application/json"));

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    // ---------- 400 ----------

    [Fact]
    public async Task Pending_Returns400_BecauseItIsNotAStatusChange()
    {
        var response = await PostAsync(Body("PENDING"));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
        Assert.Empty(_factory.NotificationRepository.All);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"bookingId":"00000000-0000-0000-0000-000000000001"}""")]
    public async Task InvalidBody_Returns400InTheErrorShape(string body)
    {
        var response = await PostAsync(body);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
    }

    [Theory]
    [InlineData("\"UNKNOWN\"")]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData("\"CONFIRMED,CANCELLED\"")]
    [InlineData("null")]
    public async Task InvalidStatus_Returns400InTheErrorShape(string status)
    {
        var body = $$"""{"bookingId":"{{_bookingId}}","businessId":"{{_businessId}}","userId":"{{_clientId}}","status":{{status}}}""";

        var response = await PostAsync(body);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
    }

    // ---------- 404 ----------

    [Fact]
    public async Task UnknownUser_Returns404()
    {
        var response = await PostAsync(Body("CONFIRMED", userId: Guid.NewGuid()));

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Empty(_factory.NotificationRepository.All);
    }

    [Theory]
    [InlineData("CONFIRMED")]
    [InlineData("CANCELLED")]
    public async Task UnknownBusiness_Returns404_WhetherOrNotTheBusinessIsARecipient(string status)
    {
        var response = await PostAsync(Body(status, businessId: Guid.NewGuid()));

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Empty(_factory.NotificationRepository.All);
    }

    // ---------- 202 ----------

    [Theory]
    [InlineData("CONFIRMED")]
    [InlineData("COMPLETED")]
    [InlineData("NO_SHOW")]
    public async Task ClientOnlyStatuses_Return202WithNoBody_AndNotifyOnlyTheClient(string status)
    {
        var response = await PostAsync(Body(status));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());

        var row = Assert.Single(_factory.NotificationRepository.All);
        AssertStored(row, _clientId);

        var push = Assert.Single(_factory.PushNotifications.Sent);
        Assert.Equal(_clientId, push.UserId);
        Assert.Equal(row.Title, push.Title);
        Assert.Equal(row.Body, push.Body);
        Assert.Equal(_bookingId.ToString(), push.Data!["bookingId"]);
        Assert.Equal(status, push.Data["status"]);
    }

    [Fact]
    public async Task Cancelled_Returns202_AndNotifiesTheClientAndTheBusinessAccount()
    {
        var response = await PostAsync(Body("CANCELLED"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var rows = _factory.NotificationRepository.All;
        Assert.Equal(2, rows.Count);
        AssertStored(rows.Single(x => x.UserId == _clientId), _clientId);
        AssertStored(rows.Single(x => x.UserId == _businessAccountId), _businessAccountId);

        Assert.Equal([_clientId, _businessAccountId], _factory.PushNotifications.Sent.Select(x => x.UserId));
        Assert.All(_factory.PushNotifications.Sent, push => Assert.Equal("CANCELLED", push.Data!["status"]));
    }

    [Fact]
    public async Task Message_BecomesTheBody()
    {
        var response = await PostAsync(Body("CONFIRMED", message: "See you at 2pm."));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("See you at 2pm.", Assert.Single(_factory.NotificationRepository.All).Body);
        Assert.Equal("See you at 2pm.", Assert.Single(_factory.PushNotifications.Sent).Body);
    }

    [Theory]
    [InlineData("CONFIRMED", "Your booking has been confirmed.")]
    [InlineData("COMPLETED", "Your booking has been completed.")]
    [InlineData("NO_SHOW", "This booking was marked as a no-show.")]
    public async Task WithoutMessage_TheBodyIsTheDefaultForTheStatus(string status, string expected)
    {
        await PostAsync(Body(status));

        Assert.Equal(expected, Assert.Single(_factory.NotificationRepository.All).Body);
    }

    [Fact]
    public async Task FailedDelivery_StillReturns202()
    {
        _factory.PushNotifications.Summary = new PushDeliverySummary(true, 0, 0, 2);

        var response = await PostAsync(Body("CANCELLED"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, _factory.NotificationRepository.All.Count);
    }

    [Fact]
    public async Task Push_IsNotTiedToTheRequest_SoBookingsTimeoutCantCutItShort()
    {
        await PostAsync(Body("CONFIRMED"));

        Assert.False(Assert.Single(_factory.PushNotifications.Sent).Token.CanBeCanceled);
    }

    private void AssertStored(Notification row, Guid expectedUserId)
    {
        Assert.Equal(expectedUserId, row.UserId);
        Assert.Equal(NotificationType.BOOKING_STATUS_CHANGED, row.Type);
        Assert.Equal("booking", row.RelatedEntityType);
        Assert.Equal(_bookingId, row.RelatedEntityId);
        Assert.False(row.IsRead);
        Assert.False(string.IsNullOrWhiteSpace(row.Title));
        Assert.False(string.IsNullOrWhiteSpace(row.Body));
    }

    private string Body(string status, Guid? userId = null, Guid? businessId = null, string? message = null) =>
        JsonSerializer.Serialize(new
        {
            bookingId = _bookingId,
            businessId = businessId ?? _businessId,
            userId = userId ?? _clientId,
            status,
            message,
        });

    private Task<HttpResponseMessage> PostAsync(string body, string? key = NotificationsApiFactory.InternalApiKey)
    {
        var client = _factory.CreateClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add("X-Internal-Api-Key", key);

        return client.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString()));
        Assert.True(error.TryGetProperty("timestamp", out _));
    }
}
