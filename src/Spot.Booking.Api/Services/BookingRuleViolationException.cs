namespace Spot.Booking.Api.Services;

/// <summary>
/// Raised when a well-formed booking request breaks a booking rule. Mapped to 422 with
/// <see cref="Code"/> as the error code (same pattern as Spot.Business.Api's InvalidBusinessHoursException).
/// </summary>
public sealed class BookingRuleViolationException(string code, string message) : Exception(message)
{
    /// <summary>Documented in contracts/spot-api.yaml (UnprocessableEntity example).</summary>
    public const string OutsideBusinessHours = "BOOKING_OUTSIDE_BUSINESS_HOURS";

    /// <summary>The contract defines no code for a start in the past; agreed for #67.</summary>
    public const string StartInPast = "BOOKING_START_IN_PAST";

    public string Code { get; } = code;
}
