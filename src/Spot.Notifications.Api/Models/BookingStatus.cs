namespace Spot.Notifications.Api.Models;

/// <summary>Matches the "BookingStatus" schema in contracts/spot-api.yaml. Only read from requests, never stored here.</summary>
public enum BookingStatus
{
    PENDING,
    CONFIRMED,
    COMPLETED,
    CANCELLED,
    NO_SHOW
}
