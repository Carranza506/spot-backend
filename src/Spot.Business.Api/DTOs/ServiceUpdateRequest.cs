using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the ServiceUpdateRequest schema in contracts/spot-api.yaml. Every field is optional
/// (PATCH): an omitted field is left untouched. <see cref="Description"/> is wrapped in
/// <see cref="Optional{T}"/> so sending it explicitly as <c>null</c> clears it, which a plain
/// nullable couldn't tell apart from "omitted" — same split as <see cref="CategoryUpdateRequest"/>.
/// The other fields aren't nullable in the contract, so a plain nullable already means "omitted".
/// Validations mirror <see cref="ServiceCreateRequest"/> but only fire when the field is present.
/// </summary>
public sealed class ServiceUpdateRequest : IValidatableObject
{
    [MaxLength(150)]
    public string? Name { get; set; }

    public Optional<string?> Description { get; set; }

    // Same bounds as ServiceCreateRequest: the contract allows a free service (minimum: 0); the
    // upper bound guards the numeric(12,2) column so an oversized price is a 400, not a 500.
    [Range(typeof(decimal), "0", "9999999999.99",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? Price { get; set; }

    [Range(1, int.MaxValue)]
    public int? DurationMinutes { get; set; }

    public bool? IsActive { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Mirrors ServiceCreateRequest: [MaxLength] doesn't catch a name that's only whitespace.
        if (Name is not null && string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("name no puede estar vacío ni contener solo espacios.", [nameof(Name)]);
    }
}
