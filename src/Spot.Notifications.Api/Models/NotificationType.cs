namespace Spot.Notifications.Api.Models;

/// <summary>
/// Matches the "NotificationType" schema in contracts/spot-api.yaml. Unlike
/// <see cref="DevicePlatform"/>, the native notification_type labels are stored UPPERCASE, exactly
/// as these member names (db/Spot.sql) — see Program.cs for the name translator that keeps them.
/// </summary>
public enum NotificationType
{
    BOOKING_STATUS_CHANGED,
    GENERAL
}
