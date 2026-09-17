using System.ComponentModel.DataAnnotations;

namespace Spot.Auth.Api.DTOs;

/// <summary>
/// Matches the "UpdateProfileRequest" schema in contracts/spot-api.yaml. Every field is
/// optional and wrapped in <see cref="Optional{T}"/> so PATCH semantics are exact: a field
/// absent from the request body is left untouched, while <c>phone</c>/<c>profilePhotoUrl</c>
/// sent explicitly as <c>null</c> are cleared. <c>email</c> and <c>role</c> are intentionally
/// not here at all — the contract forbids changing either through this endpoint.
/// </summary>
public sealed class UpdateProfileRequest : IValidatableObject
{
    public Optional<string> FirstName { get; set; }
    public Optional<string> LastName { get; set; }
    public Optional<string?> Phone { get; set; }
    public Optional<string?> ProfilePhotoUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FirstName.IsSet)
        {
            if (string.IsNullOrWhiteSpace(FirstName.Value))
                yield return new ValidationResult("firstName no puede estar vacío.", [nameof(FirstName)]);
            else if (FirstName.Value.Length > 100)
                yield return new ValidationResult("firstName no puede superar los 100 caracteres.", [nameof(FirstName)]);
        }

        if (LastName.IsSet)
        {
            if (string.IsNullOrWhiteSpace(LastName.Value))
                yield return new ValidationResult("lastName no puede estar vacío.", [nameof(LastName)]);
            else if (LastName.Value.Length > 100)
                yield return new ValidationResult("lastName no puede superar los 100 caracteres.", [nameof(LastName)]);
        }

        if (Phone.IsSet && Phone.Value?.Length > 30)
            yield return new ValidationResult("phone no puede superar los 30 caracteres.", [nameof(Phone)]);

        if (ProfilePhotoUrl.IsSet && ProfilePhotoUrl.Value is not null
            && !Uri.TryCreate(ProfilePhotoUrl.Value, UriKind.Absolute, out _))
            yield return new ValidationResult("profilePhotoUrl debe ser una URL válida.", [nameof(ProfilePhotoUrl)]);
    }
}
