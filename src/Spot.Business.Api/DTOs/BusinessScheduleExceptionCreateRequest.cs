using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the BusinessScheduleExceptionCreateRequest schema in contracts/spot-api.yaml. Only the
/// per-field format checks live here (400 via model validation); the opening-hours rules are 422s,
/// so the service checks them instead — same split as <see cref="BusinessHourInput"/>.
/// openTime, closeTime and reason accept an explicit null as well as being omitted.
/// </summary>
public sealed class BusinessScheduleExceptionCreateRequest
{
    /// <summary>System.Text.Json only reads a DateOnly written exactly as YYYY-MM-DD; anything else is a 400.</summary>
    [Required]
    public DateOnly? ExceptionDate { get; set; }

    /// <summary>Required by the contract: nullable so a missing value is a 400, not false.</summary>
    [Required]
    public bool? IsClosed { get; set; }

    [RegularExpression(BusinessHourInput.TimePattern)]
    public string? OpenTime { get; set; }

    [RegularExpression(BusinessHourInput.TimePattern)]
    public string? CloseTime { get; set; }

    [MaxLength(255)]
    public string? Reason { get; set; }
}
