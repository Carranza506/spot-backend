namespace Spot.Booking.Api.Services;

/// <summary>
/// Every business's schedule (business_hours, business_schedule_exceptions) is interpreted in this
/// zone; per-business time zones are out of scope (#66). An IANA id, so it resolves from tzdata on
/// Linux (the Docker image) and through ICU on Windows.
/// </summary>
public static class BusinessTimeZone
{
    public const string Id = "America/Costa_Rica";

    public static readonly TimeZoneInfo Instance = TimeZoneInfo.FindSystemTimeZoneById(Id);
}
