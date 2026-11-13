using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the BusinessHoursUpsertRequest schema in contracts/spot-api.yaml (1 to 7 days).</summary>
public sealed class BusinessHoursUpsertRequest : IValidatableObject
{
    [Required]
    [MinLength(1)]
    [MaxLength(7)]
    public List<BusinessHourInput> Hours { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Model validation skips null items, so "hours": [null] would otherwise reach the service.
        if (Hours.Any(hour => hour is null))
            yield return new ValidationResult("hours no puede contener elementos nulos.", [nameof(Hours)]);
    }
}
