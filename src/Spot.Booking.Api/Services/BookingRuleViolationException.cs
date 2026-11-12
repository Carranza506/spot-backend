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

    /// <summary>The contract defines no code for a transition from the wrong status; agreed for #69.</summary>
    public const string InvalidStatusTransition = "BOOKING_INVALID_STATUS_TRANSITION";

    /// <summary>Complete or no-show before the booking's start time; agreed for #146.</summary>
    public const string NotStarted = "BOOKING_NOT_STARTED";

    /// <summary>A start that isn't on availability's slot grid for the service (BUG-11).</summary>
    public const string NotOnSlot = "BOOKING_NOT_ON_SLOT";

    /// <summary>A start more than BookingService.MaxDaysAhead days after today (BUG-11).</summary>
    public const string TooFarAhead = "BOOKING_TOO_FAR_AHEAD";

    public string Code { get; } = code;
}
