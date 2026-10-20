namespace Spot.Booking.Api.DTOs;

/// <summary>
/// Query string of GET /booking/businesses/{businessId}/availability (contracts/spot-api.yaml).
/// <see cref="From"/>/<see cref="To"/> are kept as strings and parsed strictly as YYYY-MM-DD by
/// the controller, since DateOnly model binding is culture-dependent and accepts other formats.
/// A malformed <see cref="ServiceId"/> or <see cref="SlotMinutes"/> fails model binding (400).
/// </summary>
public sealed class AvailabilityQuery
{
    public const string DateFormat = "yyyy-MM-dd";
    public const int DefaultSlotMinutes = 30;
    public const int MinSlotMinutes = 5;
    public const int MaxSlotMinutes = 480;

    /// <summary>The range is inclusive on both ends and capped at this many days.</summary>
    public const int MaxRangeDays = 31;

    public string? From { get; set; }
    public string? To { get; set; }
    public Guid? ServiceId { get; set; }
    public int? SlotMinutes { get; set; }
}
