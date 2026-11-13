using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the ServiceUpdateRequest schema in contracts/spot-api.yaml. Every field is optional
/// (PATCH) and wrapped in <see cref="Optional{T}"/> so an omitted field is left untouched, while
/// an explicit value is applied — same approach as Spot.Auth.Api's UpdateProfileRequest. Only
/// <see cref="Description"/> is nullable in the contract (<c>null</c> clears it); the others aren't,
/// so sending them as <c>null</c> is rejected with 400 (see <see cref="Validate"/>). Validations
/// mirror <see cref="ServiceCreateRequest"/> and only fire when the field is present.
/// </summary>
public sealed class ServiceUpdateRequest : IValidatableObject
{
    public Optional<string> Name { get; set; }

    public Optional<string?> Description { get; set; }

    public Optional<decimal?> Price { get; set; }

    public Optional<int?> DurationMinutes { get; set; }

    public Optional<bool?> IsActive { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name.IsSet)
        {
            // null or whitespace name is rejected (null isn't allowed by the contract for name).
            if (string.IsNullOrWhiteSpace(Name.Value))
                yield return new ValidationResult("name no puede estar vacío ni contener solo espacios.", [nameof(Name)]);
            else if (Name.Value.Trim().Length > 150)
                yield return new ValidationResult("name no puede superar 150 caracteres.", [nameof(Name)]);
        }

        if (Price.IsSet)
        {
            if (Price.Value is null)
                yield return new ValidationResult("price no puede ser null.", [nameof(Price)]);
            // The contract allows a free service (minimum: 0). The upper bound guards numeric(12,2).
            else if (Price.Value is < 0 or > 9_999_999_999.99m)
                yield return new ValidationResult("price debe ser mayor o igual a 0.", [nameof(Price)]);
        }

        if (DurationMinutes.IsSet)
        {
            if (DurationMinutes.Value is null)
                yield return new ValidationResult("durationMinutes no puede ser null.", [nameof(DurationMinutes)]);
            else if (DurationMinutes.Value < 1)
                yield return new ValidationResult("durationMinutes debe ser mayor o igual a 1.", [nameof(DurationMinutes)]);
        }

        if (IsActive.IsSet && IsActive.Value is null)
            yield return new ValidationResult("isActive no puede ser null.", [nameof(IsActive)]);
    }
}
