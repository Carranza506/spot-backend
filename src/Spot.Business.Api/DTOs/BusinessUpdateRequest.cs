using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the BusinessUpdateRequest schema in contracts/spot-api.yaml, shared by
/// PATCH /business/businesses/{businessId} and PATCH /business/businesses/me. Every field is
/// optional. The nullable ones are wrapped in <see cref="Optional{T}"/> so omitting a field leaves
/// it untouched while sending it as <c>null</c> clears it; <see cref="Name"/>/<see cref="IsActive"/>
/// aren't nullable in the contract, so a plain nullable property already means "omitted" — same
/// split as <see cref="CategoryUpdateRequest"/>.
/// </summary>
public sealed class BusinessUpdateRequest : IValidatableObject
{
    public string? Name { get; set; }

    public Optional<string?> Description { get; set; }

    public Optional<string?> LegalName { get; set; }

    public Optional<string?> Email { get; set; }

    public Optional<string?> Phone { get; set; }

    public Optional<string?> Website { get; set; }

    public Optional<string?> LogoUrl { get; set; }

    public bool? IsActive { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null)
        {
            foreach (var result in BusinessFieldRules.ValidateName(Name, nameof(Name)))
                yield return result;
        }

        // An unset Optional carries a null Value, which every rule already treats as "nothing to check".
        foreach (var result in BusinessFieldRules.ValidateOptionalFields(
            LegalName.Value, Email.Value, Phone.Value, Website.Value, LogoUrl.Value))
            yield return result;
    }
}
