using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;

namespace Spot.Notifications.Api.Services.Push;

public sealed class PushNotificationService(
    IDeviceTokenRepository deviceTokens,
    IPushSender sender,
    ILogger<PushNotificationService> logger) : IPushNotificationService
{
    /// <summary>
    /// Android only (#80's scope). The Expo app registers native tokens: on Android that's an FCM
    /// token, but on iOS it's an APNs token FCM can't deliver to, and web push needs its own setup.
    /// </summary>
    private const DevicePlatform DeliveredPlatform = DevicePlatform.ANDROID;

    public async Task<PushDeliverySummary> SendToUserAsync(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        var tokens = await deviceTokens.ListActiveByUserAsync(userId, DeliveredPlatform, ct);
        int sent = 0, deactivated = 0, failed = 0;

        foreach (var token in tokens)
        {
            PushSendResult result;
            try
            {
                result = await sender.SendAsync(new PushMessage(token.Token, title, body, data), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // One device's failure must not cost the others their push. Only the row id and the
                // exception type: its message could carry the token.
                logger.LogWarning("Push to device token {DeviceTokenId} failed unexpectedly ({ExceptionType}).", token.Id, ex.GetType().Name);
                failed++;
                continue;
            }

            switch (result.Outcome)
            {
                case PushSendOutcome.NotConfigured:
                    // Same answer for every token: nothing can be sent, so don't try the rest.
                    logger.LogInformation("Push to user {UserId} skipped: push delivery isn't configured.", userId);
                    return PushDeliverySummary.NotConfigured;

                case PushSendOutcome.Sent:
                    sent++;
                    break;

                case PushSendOutcome.InvalidToken:
                    try
                    {
                        await deviceTokens.DeactivateAsync(token, ct);
                        deactivated++;
                        // The row id, never the token itself.
                        logger.LogInformation("Device token {DeviceTokenId} deactivated: FCM {ErrorCode}.", token.Id, result.ErrorCode);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        // Still dead, so the next push retries the deactivation.
                        logger.LogWarning("Couldn't deactivate device token {DeviceTokenId} ({ExceptionType}).", token.Id, ex.GetType().Name);
                        failed++;
                    }
                    break;

                default:
                    failed++;
                    break;
            }
        }

        if (tokens.Count > 0)
            logger.LogInformation(
                "Push to user {UserId}: {Sent} sent, {Deactivated} deactivated, {Failed} failed.", userId, sent, deactivated, failed);

        return new PushDeliverySummary(true, sent, deactivated, failed);
    }
}
