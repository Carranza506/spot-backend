using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Services.Push;
using Spot.Notifications.Api.Tests.Fakes;
using Spot.Notifications.Api.Tests.TestSupport;

namespace Spot.Notifications.Api.Tests.Services.Push;

public sealed class PushNotificationServiceTests
{
    private readonly FakeDeviceTokenRepository _repo = new();
    private readonly FakePushSender _sender = new();
    private readonly CapturingLogger<PushNotificationService> _logger = new();
    private readonly Guid _userId = Guid.NewGuid();

    private PushNotificationService CreateService() => new(_repo, _sender, _logger);

    private DeviceToken Seed(string token, DevicePlatform platform = DevicePlatform.ANDROID, bool isActive = true, Guid? userId = null) =>
        _repo.Seed(new DeviceToken { UserId = userId ?? _userId, Token = token, Platform = platform, IsActive = isActive });

    [Fact]
    public async Task SendToUserAsync_SendsToEachActiveAndroidTokenOfTheUser()
    {
        Seed("android-1");
        Seed("android-2");
        Seed("android-inactive", isActive: false);
        Seed("ios-apns", DevicePlatform.IOS);
        Seed("web", DevicePlatform.WEB);
        Seed("someone-else", userId: Guid.NewGuid());
        var data = new Dictionary<string, string> { ["bookingId"] = "abc" };

        var summary = await CreateService().SendToUserAsync(_userId, "Título", "Cuerpo", data);

        Assert.Equal(new PushDeliverySummary(true, 2, 0, 0), summary);
        Assert.Equal(["android-1", "android-2"], _sender.Sent.Select(m => m.DeviceToken).Order());
        Assert.All(_sender.Sent, m =>
        {
            Assert.Equal("Título", m.Title);
            Assert.Equal("Cuerpo", m.Body);
            Assert.Same(data, m.Data);
        });
    }

    [Fact]
    public async Task SendToUserAsync_DeadToken_IsDeactivated_TheOthersStayActive()
    {
        var dead = Seed("dead");
        var alive = Seed("alive");
        _sender.Respond = token => token == "dead"
            ? new PushSendResult(PushSendOutcome.InvalidToken, "UNREGISTERED")
            : PushSendResult.Sent;

        var summary = await CreateService().SendToUserAsync(_userId, "T", "B");

        Assert.Equal(new PushDeliverySummary(true, 1, 1, 0), summary);
        Assert.False(dead.IsActive);
        Assert.True(alive.IsActive);
    }

    [Theory]
    [InlineData(PushSendOutcome.TransientFailure, "UNAVAILABLE")]
    [InlineData(PushSendOutcome.TransientFailure, "FCM_TIMEOUT")]
    [InlineData(PushSendOutcome.Rejected, "INVALID_ARGUMENT")]
    public async Task SendToUserAsync_TransientOrRejected_KeepsTheToken(PushSendOutcome outcome, string errorCode)
    {
        var token = Seed("android-1");
        _sender.Respond = _ => new PushSendResult(outcome, errorCode);

        var summary = await CreateService().SendToUserAsync(_userId, "T", "B");

        Assert.Equal(new PushDeliverySummary(true, 0, 0, 1), summary);
        Assert.True(token.IsActive);
    }

    [Fact]
    public async Task SendToUserAsync_NotConfigured_StopsAtTheFirstToken_AndDeactivatesNothing()
    {
        Seed("android-1");
        Seed("android-2");
        _sender.Respond = _ => PushSendResult.NotConfigured;

        var summary = await CreateService().SendToUserAsync(_userId, "T", "B");

        Assert.Equal(PushDeliverySummary.NotConfigured, summary);
        Assert.Single(_sender.Sent);
        Assert.All(_repo.All, t => Assert.True(t.IsActive));
    }

    [Fact]
    public async Task SendToUserAsync_NoTokens_SendsNothing()
    {
        var summary = await CreateService().SendToUserAsync(_userId, "T", "B");

        Assert.Equal(new PushDeliverySummary(true, 0, 0, 0), summary);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task SendToUserAsync_Logs_UseTheTokenRowId_NeverTheTokenItself()
    {
        var dead = Seed("dead-token-value-must-never-be-logged");
        Seed("live-token-value-must-never-be-logged");
        _sender.Respond = token => token.StartsWith("dead")
            ? new PushSendResult(PushSendOutcome.InvalidToken, "UNREGISTERED")
            : PushSendResult.Sent;

        await CreateService().SendToUserAsync(_userId, "T", "B");

        Assert.Contains(dead.Id.ToString(), _logger.AllText);
        Assert.DoesNotContain("token-value-must-never-be-logged", _logger.AllText);
    }

    [Fact]
    public async Task SendToUserAsync_DeactivateFails_CountsItAsFailed_AndTheOtherTokensAreStillProcessed()
    {
        var deadButStuck = Seed("dead-1");
        var dead = Seed("dead-2");
        var alive = Seed("alive");
        _sender.Respond = token => token.StartsWith("dead")
            ? new PushSendResult(PushSendOutcome.InvalidToken, "UNREGISTERED")
            : PushSendResult.Sent;
        _repo.FailDeactivateWith = token => token.Token == "dead-1"
            ? new InvalidOperationException("database unavailable while saving dead-1")
            : null;

        var summary = await CreateService().SendToUserAsync(_userId, "T", "B");

        Assert.Equal(new PushDeliverySummary(true, 1, 1, 1), summary);
        Assert.Equal(["alive", "dead-1", "dead-2"], _sender.Sent.Select(m => m.DeviceToken).Order());
        Assert.True(deadButStuck.IsActive);
        Assert.False(dead.IsActive);
        Assert.True(alive.IsActive);
        Assert.Contains(deadButStuck.Id.ToString(), _logger.AllText);
        // The exception's message names the token: only its type may reach the log.
        Assert.DoesNotContain("dead-1", _logger.AllText);
    }

    [Fact]
    public async Task SendToUserAsync_SenderThrowsUnexpectedly_TheOtherTokensStillGetThePush()
    {
        var broken = Seed("android-broken");
        Seed("android-1");
        Seed("android-2");
        _sender.Respond = token => token == "android-broken"
            ? throw new IOException("connection reset while sending to android-broken")
            : PushSendResult.Sent;

        var summary = await CreateService().SendToUserAsync(_userId, "T", "B");

        Assert.Equal(new PushDeliverySummary(true, 2, 0, 1), summary);
        Assert.Equal(["android-1", "android-2", "android-broken"], _sender.Sent.Select(m => m.DeviceToken).Order());
        Assert.True(broken.IsActive);
        Assert.Contains(broken.Id.ToString(), _logger.AllText);
        Assert.DoesNotContain("android-broken", _logger.AllText);
    }

    [Fact]
    public async Task SendToUserAsync_CallerCancels_StillPropagates()
    {
        Seed("android-1");
        Seed("android-2");
        using var cts = new CancellationTokenSource();
        _sender.Respond = _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().SendToUserAsync(_userId, "T", "B", ct: cts.Token));
        Assert.Single(_sender.Sent);
    }
}
