using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the ServiceCreateRequest schema in contracts/spot-api.yaml. Has no BusinessId or
/// IsActive on purpose: the business comes from the route, and a new service always starts active.
/// <see cref="Price"/>/<see cref="DurationMinutes"/> are nullable only so [Required] can tell a
/// missing field apart from an explicit 0 — otherwise an omitted price would bind as a free service.
/// </summary>
public sealed class ServiceCreateRequest : IValidatableObject
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    // The contract allows a free service (minimum: 0). The upper bound isn't in the contract, but
    // services.price is numeric(12,2): without it an oversized price would surface as a 500, not a 400.
    [Required]
    [Range(typeof(decimal), "0", "9999999999.99",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? Price { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int? DurationMinutes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already rejects null/"" — this catches the "   " case it doesn't.
        if (!string.IsNullOrEmpty(Name) && string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("name no puede estar vacío ni contener solo espacios.", [nameof(Name)]);
    }
}
