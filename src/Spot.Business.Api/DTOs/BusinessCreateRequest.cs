using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the BusinessCreateRequest schema in contracts/spot-api.yaml. Has no AccountId, Slug
/// or IsActive on purpose: the account comes from the JWT <c>sub</c>, the slug is generated from
/// <see cref="Name"/>, and a new business always starts active — any such field in the body is
/// simply ignored.
/// </summary>
public sealed class BusinessCreateRequest : IValidatableObject
{
    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public string? LegalName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Website { get; set; }

    public string? LogoUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in BusinessFieldRules.ValidateName(Name, nameof(Name)))
            yield return result;

        foreach (var result in BusinessFieldRules.ValidateOptionalFields(LegalName, Email, Phone, Website, LogoUrl))
            yield return result;
    }
}
