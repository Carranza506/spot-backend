using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Spot.Notifications.Api.Options;

namespace Spot.Notifications.Api.Services.Push;

/// <summary>
/// <see cref="IPushSender"/> on FCM HTTP v1 (POST {BaseUrl}/projects/{ProjectId}/messages:send),
/// same pattern as Spot.AiSearch.Api's GeminiClient: typed HttpClient, own timeout, and failures
/// reported, never thrown. Device tokens and access tokens are never logged: only error codes.
/// </summary>
/// <remarks>
/// Error codes come from the FcmError detail (type.googleapis.com/google.firebase.fcm.v1.FcmError),
/// as the official Firebase Admin SDKs read them. Per Firebase's token-management guide, UNREGISTERED
/// means the token is dead; SENDER_ID_MISMATCH means it belongs to another project, so it can never
/// work here either. INVALID_ARGUMENT is deliberately not a dead token: the same guide warns it's
/// also returned for payload problems.
/// </remarks>
public sealed class FcmPushSender(
    HttpClient httpClient,
    IOptions<FcmOptions> options,
    IFcmAccessTokenProvider accessTokens,
    ILogger<FcmPushSender> logger) : IPushSender
{
    private const string FcmErrorType = "type.googleapis.com/google.firebase.fcm.v1.FcmError";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken ct = default) =>
        SendAsync(new FcmMessage(message.DeviceToken, null, new FcmNotification(message.Title, message.Body), message.Data),
            forceValidateOnly: false, ct);

    /// <summary>
    /// Validates a message to <paramref name="topic"/> without delivering it (validate_only), so the
    /// credentials and payload can be checked against Google without a real device — the live test.
    /// </summary>
    public Task<PushSendResult> DryRunToTopicAsync(string topic, string title, string body, CancellationToken ct = default) =>
        SendAsync(new FcmMessage(null, topic, new FcmNotification(title, body), null), forceValidateOnly: true, ct);

    private async Task<PushSendResult> SendAsync(FcmMessage message, bool forceValidateOnly, CancellationToken ct)
    {
        FcmOptions fcmOptions;
        try
        {
            fcmOptions = options.Value;
        }
        catch (OptionsValidationException ex)
        {
            // The validator's messages name settings, never their values.
            logger.LogWarning("Push delivery skipped: the Fcm configuration is invalid ({Failures}).", string.Join(" ", ex.Failures));
            return PushSendResult.NotConfigured;
        }

        if (!fcmOptions.IsConfigured)
            return PushSendResult.NotConfigured;

        string accessToken;
        try
        {
            accessToken = await accessTokens.GetAccessTokenAsync(ct);
        }
        catch (FcmNotConfiguredException ex)
        {
            logger.LogWarning("Push delivery skipped: {Reason}", ex.Message);
            return PushSendResult.NotConfigured;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't obtain an FCM access token ({ExceptionType}).", ex.GetType().Name);
            return new PushSendResult(PushSendOutcome.TransientFailure, "FCM_AUTH_FAILED");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(fcmOptions.TimeoutSeconds));

        try
        {
            var requestUri = $"{fcmOptions.BaseUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(fcmOptions.ProjectId)}/messages:send";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(
                    new FcmSendRequest(message, forceValidateOnly || fcmOptions.ValidateOnly ? true : null), options: SerializerOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.SendAsync(request, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
                return PushSendResult.Sent;

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var errorCode = ReadErrorCode(body) ?? $"HTTP_{(int)response.StatusCode}";
            var outcome = Classify(errorCode);

            logger.LogWarning("FCM answered {StatusCode} {ErrorCode} ({Outcome}).", (int)response.StatusCode, errorCode, outcome);
            return new PushSendResult(outcome, errorCode);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("FCM call timed out after {TimeoutSeconds}s.", fcmOptions.TimeoutSeconds);
            return new PushSendResult(PushSendOutcome.TransientFailure, "FCM_TIMEOUT");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("FCM call failed ({HttpRequestError}).", ex.HttpRequestError);
            return new PushSendResult(PushSendOutcome.TransientFailure, "FCM_REQUEST_FAILED");
        }
    }

    private static PushSendOutcome Classify(string errorCode) => errorCode switch
    {
        "UNREGISTERED" or "SENDER_ID_MISMATCH" => PushSendOutcome.InvalidToken,
        "INVALID_ARGUMENT" => PushSendOutcome.Rejected,
        // QUOTA_EXCEEDED, UNAVAILABLE, INTERNAL, THIRD_PARTY_AUTH_ERROR, APNS_AUTH_ERROR, an auth
        // failure on our side, or anything unknown: never the token's fault, so keep it.
        _ => PushSendOutcome.TransientFailure,
    };

    /// <summary>The FcmError detail's errorCode, else the Google error status (e.g. UNAUTHENTICATED), else null.</summary>
    private static string? ReadErrorCode(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<GoogleErrorResponse>(body, SerializerOptions)?.Error;
            return error?.Details?.FirstOrDefault(d => d.Type == FcmErrorType)?.ErrorCode ?? error?.Status;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record FcmSendRequest(FcmMessage Message, bool? ValidateOnly);

    private sealed record FcmMessage(string? Token, string? Topic, FcmNotification Notification, IReadOnlyDictionary<string, string>? Data);

    private sealed record FcmNotification(string Title, string Body);

    private sealed record GoogleErrorResponse(GoogleError? Error);

    private sealed record GoogleError(string? Status, IReadOnlyList<GoogleErrorDetail>? Details);

    private sealed record GoogleErrorDetail([property: JsonPropertyName("@type")] string? Type, string? ErrorCode);
}
