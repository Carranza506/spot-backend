namespace Spot.Notifications.Api.Services.Push;

/// <summary>A push to one device token. <see cref="Data"/> values must be strings (FCM's data payload).</summary>
public sealed record PushMessage(string DeviceToken, string Title, string Body, IReadOnlyDictionary<string, string>? Data = null);

public enum PushSendOutcome
{
    /// <summary>Accepted by the provider (or validated, with validate_only).</summary>
    Sent,

    /// <summary>The token will never work again (unregistered, or from another sender): deactivate it.</summary>
    InvalidToken,

    /// <summary>
    /// The request itself was rejected (INVALID_ARGUMENT). Not treated as a dead token: FCM also
    /// returns it for a bad payload, and a payload bug must never deactivate everyone's tokens.
    /// </summary>
    Rejected,

    /// <summary>Temporary (quota, provider unavailable, timeout, auth): try again later, keep the token.</summary>
    TransientFailure,

    /// <summary>Push delivery isn't configured (no project or no usable key): nothing was sent.</summary>
    NotConfigured,
}

/// <summary><paramref name="ErrorCode"/> is the provider's code (e.g. UNREGISTERED), or a local one (FCM_TIMEOUT…).</summary>
public sealed record PushSendResult(PushSendOutcome Outcome, string? ErrorCode = null)
{
    public static readonly PushSendResult Sent = new(PushSendOutcome.Sent);
    public static readonly PushSendResult NotConfigured = new(PushSendOutcome.NotConfigured);
}

/// <summary>
/// Delivers a push to one device (#80). Behind an interface so the provider (FCM HTTP v1 today)
/// can be swapped without touching its callers. Never throws for a delivery failure: it reports it.
/// </summary>
public interface IPushSender
{
    Task<PushSendResult> SendAsync(PushMessage message, CancellationToken ct = default);
}
