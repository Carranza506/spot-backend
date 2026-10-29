using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Notifications.Api.Options;
using Spot.Notifications.Api.Services.Push;
using Spot.Notifications.Api.Tests.Fakes;
using Spot.Notifications.Api.Tests.TestSupport;

namespace Spot.Notifications.Api.Tests.Services.Push;

public sealed class FcmPushSenderTests
{
    private const string DeviceToken = "dGVzdC1kZXZpY2UtdG9rZW4tbXVzdC1uZXZlci1iZS1sb2dnZWQ";

    private static FcmOptions Options => new()
    {
        ProjectId = "spot-test-project",
        CredentialsFile = "/run/secrets/fcm_credentials",
        BaseUrl = "https://fcm.fake.googleapis.com/v1",
        TimeoutSeconds = 5,
    };

    private static readonly PushMessage Message = new(
        DeviceToken, "Reserva confirmada", "Tu cita es mañana a las 9:00 a. m.",
        new Dictionary<string, string> { ["bookingId"] = "2c3d4e5f-6071-4a2b-8c1d-9e0f1a2b3c4e" });

    private static FcmPushSender CreateSender(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        out FakeHttpMessageHandler fakeHandler,
        out CapturingLogger<FcmPushSender> logger,
        FcmOptions? options = null,
        FakeFcmAccessTokenProvider? accessTokens = null)
    {
        fakeHandler = new FakeHttpMessageHandler(handler);
        logger = new CapturingLogger<FcmPushSender>();
        return new FcmPushSender(
            new HttpClient(fakeHandler),
            Microsoft.Extensions.Options.Options.Create(options ?? Options),
            accessTokens ?? new FakeFcmAccessTokenProvider(),
            logger);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { name = "projects/spot-test-project/messages/0:123" }) };

    /// <summary>The error body FCM HTTP v1 returns, with the FcmError detail the Admin SDKs read.</summary>
    private static HttpResponseMessage FcmError(HttpStatusCode statusCode, string status, string? errorCode) =>
        new(statusCode)
        {
            Content = JsonContent.Create(new
            {
                error = new
                {
                    code = (int)statusCode,
                    message = "Requested entity was not found.",
                    status,
                    details = errorCode is null
                        ? Array.Empty<object>()
                        : new object[]
                        {
                            new Dictionary<string, string>
                            {
                                ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError",
                                ["errorCode"] = errorCode,
                            },
                        },
                },
            }),
        };

    [Fact]
    public async Task SendAsync_Accepted_PostsTheMessageToTheProjectWithTheAccessToken()
    {
        string? body = null;
        var sender = CreateSender(async (request, _) =>
        {
            // Read it here: the sender disposes the request once the response comes back.
            body = await request.Content!.ReadAsStringAsync();
            return Ok();
        }, out var handler, out _);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.Sent, result.Outcome);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://fcm.fake.googleapis.com/v1/projects/spot-test-project/messages:send", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(FakeFcmAccessTokenProvider.AccessToken, handler.LastRequest.Headers.Authorization.Parameter);

        var message = JsonDocument.Parse(body!).RootElement.GetProperty("message");
        Assert.Equal(DeviceToken, message.GetProperty("token").GetString());
        Assert.Equal("Reserva confirmada", message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("Tu cita es mañana a las 9:00 a. m.", message.GetProperty("notification").GetProperty("body").GetString());
        Assert.Equal("2c3d4e5f-6071-4a2b-8c1d-9e0f1a2b3c4e", message.GetProperty("data").GetProperty("bookingId").GetString());
        Assert.False(message.TryGetProperty("topic", out _));
        // Delivered for real unless validate_only is configured.
        Assert.False(JsonDocument.Parse(body!).RootElement.TryGetProperty("validateOnly", out _));
    }

    [Fact]
    public async Task SendAsync_ValidateOnlyConfigured_AsksFcmNotToDeliver()
    {
        string? body = null;
        var options = Options;
        options.ValidateOnly = true;
        var sender = CreateSender(async (request, _) => { body = await request.Content!.ReadAsStringAsync(); return Ok(); },
            out _, out _, options);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.Sent, result.Outcome);
        Assert.True(JsonDocument.Parse(body!).RootElement.GetProperty("validateOnly").GetBoolean());
    }

    [Fact]
    public async Task DryRunToTopicAsync_TargetsTheTopic_AlwaysValidateOnly()
    {
        string? body = null;
        var sender = CreateSender(async (request, _) => { body = await request.Content!.ReadAsStringAsync(); return Ok(); },
            out _, out _);

        var result = await sender.DryRunToTopicAsync("spot-demo", "Prueba", "Sin entregar");

        Assert.Equal(PushSendOutcome.Sent, result.Outcome);
        var root = JsonDocument.Parse(body!).RootElement;
        Assert.True(root.GetProperty("validateOnly").GetBoolean());
        Assert.Equal("spot-demo", root.GetProperty("message").GetProperty("topic").GetString());
        Assert.False(root.GetProperty("message").TryGetProperty("token", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND", "UNREGISTERED")]
    [InlineData(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "SENDER_ID_MISMATCH")]
    public async Task SendAsync_DeadToken_ReportsInvalidToken(HttpStatusCode statusCode, string status, string errorCode)
    {
        var sender = CreateSender((_, _) => Task.FromResult(FcmError(statusCode, status, errorCode)), out _, out _);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.InvalidToken, result.Outcome);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_InvalidArgument_IsRejected_NotADeadToken()
    {
        // FCM also answers INVALID_ARGUMENT for a bad payload, which must never deactivate tokens.
        var sender = CreateSender((_, _) => Task.FromResult(FcmError(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "INVALID_ARGUMENT")),
            out _, out _);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.Rejected, result.Outcome);
        Assert.Equal("INVALID_ARGUMENT", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "QUOTA_EXCEEDED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", "UNAVAILABLE")]
    [InlineData(HttpStatusCode.InternalServerError, "INTERNAL", "INTERNAL")]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "THIRD_PARTY_AUTH_ERROR")]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", null)]
    public async Task SendAsync_TemporaryOrNotTheTokensFault_IsATransientFailure(HttpStatusCode statusCode, string status, string? errorCode)
    {
        var sender = CreateSender((_, _) => Task.FromResult(FcmError(statusCode, status, errorCode)), out _, out _);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.TransientFailure, result.Outcome);
        // Without an FcmError detail, the Google status is the code.
        Assert.Equal(errorCode ?? status, result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_UnreadableErrorBody_IsATransientFailure()
    {
        var sender = CreateSender((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad Gateway</html>", Encoding.UTF8, "text/html"),
        }), out _, out _);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.TransientFailure, result.Outcome);
        Assert.Equal("HTTP_502", result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_RequestExceedsTimeout_IsATransientFailure()
    {
        var options = Options;
        options.TimeoutSeconds = 1;
        var sender = CreateSender(async (_, ct) =>
        {
            // Never completes within the configured timeout; honors cancellation like a real stalled call.
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Ok();
        }, out _, out _, options);

        var result = await sender.SendAsync(Message);

        Assert.Equal(new PushSendResult(PushSendOutcome.TransientFailure, "FCM_TIMEOUT"), result);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_IsATransientFailure()
    {
        var sender = CreateSender((_, _) => throw new HttpRequestException("No such host is known. (fcm.googleapis.com:443)"), out _, out _);

        var result = await sender.SendAsync(Message);

        Assert.Equal(new PushSendResult(PushSendOutcome.TransientFailure, "FCM_REQUEST_FAILED"), result);
    }

    [Fact]
    public async Task SendAsync_CallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var sender = CreateSender(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Ok(); }, out _, out _);

        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(Message, cts.Token));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "/run/secrets/fcm_credentials")]
    [InlineData("spot-test-project", "")]
    public async Task SendAsync_NotConfigured_SendsNothing_AndNeverAsksForAToken(string projectId, string credentialsFile)
    {
        var accessTokens = new FakeFcmAccessTokenProvider();
        var sender = CreateSender((_, _) => Task.FromResult(Ok()), out var handler, out _,
            new FcmOptions { ProjectId = projectId, CredentialsFile = credentialsFile }, accessTokens);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.Null(handler.LastRequest);
        Assert.Equal(0, accessTokens.Calls);
    }

    [Fact]
    public async Task SendAsync_UnusableKeyFile_IsNotConfigured()
    {
        var accessTokens = new FakeFcmAccessTokenProvider
        {
            FailWith = new FcmNotConfiguredException("The FCM key file isn't a valid service account key."),
        };
        var sender = CreateSender((_, _) => Task.FromResult(Ok()), out var handler, out _, accessTokens: accessTokens);

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SendAsync_AccessTokenRequestFails_IsATransientFailure()
    {
        var accessTokens = new FakeFcmAccessTokenProvider { FailWith = new HttpRequestException("oauth2.googleapis.com unreachable") };
        var sender = CreateSender((_, _) => Task.FromResult(Ok()), out var handler, out _, accessTokens: accessTokens);

        var result = await sender.SendAsync(Message);

        Assert.Equal(new PushSendResult(PushSendOutcome.TransientFailure, "FCM_AUTH_FAILED"), result);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SendAsync_Failures_NeverLogTheDeviceTokenOrTheAccessToken()
    {
        // Every logging path: a dead token, a rejection, a transient error, a timeout and a network failure.
        HttpResponseMessage[] responses =
        [
            FcmError(HttpStatusCode.NotFound, "NOT_FOUND", "UNREGISTERED"),
            FcmError(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "INVALID_ARGUMENT"),
            FcmError(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", "UNAVAILABLE"),
        ];
        var call = 0;
        var options = Options;
        options.TimeoutSeconds = 1;
        var sender = CreateSender(async (_, ct) =>
        {
            var i = call++;
            if (i < responses.Length) return responses[i];
            if (i == responses.Length) { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Ok(); }
            throw new HttpRequestException($"failed sending to {DeviceToken}");
        }, out _, out var logger, options);

        for (var i = 0; i < responses.Length + 2; i++)
            await sender.SendAsync(Message);

        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(DeviceToken, logger.AllText);
        Assert.DoesNotContain(FakeFcmAccessTokenProvider.AccessToken, logger.AllText);
    }
}
