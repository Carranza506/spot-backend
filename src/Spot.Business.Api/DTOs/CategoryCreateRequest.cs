using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the CategoryCreateRequest schema in contracts/spot-api.yaml.</summary>
public sealed class CategoryCreateRequest : IValidatableObject
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = default!;

    /// <summary>Null creates a root category; an existing category id creates a subcategory under it.</summary>
    public Guid? ParentCategoryId { get; set; }

    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already rejects null/"" — this catches the "   " case it doesn't.
        if (!string.IsNullOrEmpty(Name) && string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("name no puede estar vacío ni contener solo espacios.", [nameof(Name)]);
    }
}
