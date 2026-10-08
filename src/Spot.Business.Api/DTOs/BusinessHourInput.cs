using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the BusinessHourInput schema in contracts/spot-api.yaml. Only the per-field format
/// checks live here (400 via model validation). The rules that span several fields or items —
/// duplicate day, open day without times, openTime >= closeTime — are 422s, so BusinessService
/// checks them instead: any DataAnnotations failure would become a 400.
/// </summary>
public sealed class BusinessHourInput
{
    /// <summary>
    /// The contract's HH:mm:ss pattern, with [0-9] instead of \d so non-ASCII digits are rejected too.
    /// The times are bound as strings on purpose: System.Text.Json's TimeOnly also accepts "09:00",
    /// "9:00:00" and fractional seconds, none of which the contract allows.
    /// </summary>
    public const string TimePattern = "^([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]$";

    public const string TimeFormat = "HH:mm:ss";

    /// <summary>Nullable only so [Required] can tell "missing" apart from 0 (Sunday).</summary>
    [Required]
    [Range(0, 6)]
    public int? DayOfWeek { get; set; }

    [RegularExpression(TimePattern)]
    public string? OpenTime { get; set; }

    [RegularExpression(TimePattern)]
    public string? CloseTime { get; set; }

    /// <summary>Required by the contract with no default: nullable so a missing value is a 400, not false.</summary>
    [Required]
    public bool? IsClosed { get; set; }
}
