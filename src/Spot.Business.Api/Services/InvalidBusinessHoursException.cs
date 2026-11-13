namespace Spot.Business.Api.Services;

/// <summary>
/// Raised when a well-formed PUT .../hours body breaks a schedule rule. Mapped to 422 with
/// <see cref="Code"/> as the error code documented in contracts/spot-api.yaml.
/// </summary>
public sealed class InvalidBusinessHoursException(string code, string message) : Exception(message)
{
    public const string DuplicateDayOfWeek = "DUPLICATE_DAY_OF_WEEK";
    public const string MissingOpeningHours = "MISSING_OPENING_HOURS";
    public const string InvalidTimeRange = "INVALID_TIME_RANGE";

    public string Code { get; } = code;
}
