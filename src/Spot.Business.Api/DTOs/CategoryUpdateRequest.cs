using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the CategoryUpdateRequest schema in contracts/spot-api.yaml. Every field is optional.
/// <see cref="ParentCategoryId"/> and <see cref="Description"/> are wrapped in
/// <see cref="Optional{T}"/> so PATCH semantics are exact: omitting one of them leaves it
/// untouched, while sending it explicitly as <c>null</c> clears it — most notably,
/// <c>{"parentCategoryId": null}</c> is how a subcategory gets promoted back to root.
/// <see cref="Name"/>/<see cref="IsActive"/> don't need the wrapper: neither is nullable in the
/// contract, so a plain nullable property already unambiguously means "omitted" when null.
/// </summary>
public sealed class CategoryUpdateRequest : IValidatableObject
{
    [MaxLength(100)]
    public string? Name { get; set; }

    public Optional<Guid?> ParentCategoryId { get; set; }

    public Optional<string?> Description { get; set; }

    public bool? IsActive { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("name no puede estar vacío ni contener solo espacios.", [nameof(Name)]);
    }
}
