namespace Spot.Notifications.Api.Services.Push;

/// <summary>
/// What happened to one push to a user. <see cref="Configured"/> is false when push delivery isn't
/// configured, in which case nothing was attempted.
/// </summary>
public sealed record PushDeliverySummary(bool Configured, int Sent, int Deactivated, int Failed)
{
    public static readonly PushDeliverySummary NotConfigured = new(false, 0, 0, 0);
}

/// <summary>
/// Sends a push to every active device of a user (#80). The delivery mechanism only: the trigger
/// (e.g. the internal booking-status-changed endpoint, #79) and the stored Notification row belong
/// to its callers.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Sends to each of <paramref name="userId"/>'s active Android tokens and deactivates the ones FCM
    /// reports dead. Never throws for a delivery failure; a user with no tokens is a no-op.
    /// </summary>
    Task<PushDeliverySummary> SendToUserAsync(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default);
}
