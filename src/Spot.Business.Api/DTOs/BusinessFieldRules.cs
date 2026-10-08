using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Field rules shared by <see cref="BusinessCreateRequest"/> and <see cref="BusinessUpdateRequest"/>.
/// Hand-rolled instead of DataAnnotations attributes because most update fields are wrapped in
/// <see cref="Optional{T}"/>, which [MaxLength]/[EmailAddress] can't see through. Values are
/// validated as they'll be stored — trimmed, with blank optional fields becoming null (see
/// <see cref="Normalize"/>).
/// </summary>
internal static class BusinessFieldRules
{
    public const int NameMaxLength = 150;
    public const int LegalNameMaxLength = 200;
    public const int PhoneMaxLength = 30;

    // Not in the contract's schema, but businesses.email is varchar(255): without this an
    // oversized email would reach the database and surface as a 500 instead of a 400.
    public const int EmailMaxLength = 255;

    private static readonly EmailAddressAttribute EmailAttribute = new();

    /// <summary>Trims; blank becomes null — same convention as CategoryService's description.</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IEnumerable<ValidationResult> ValidateName(string name, string member)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield return new ValidationResult("name no puede estar vacío ni contener solo espacios.", [member]);
        else if (name.Trim().Length > NameMaxLength)
            yield return new ValidationResult($"name no puede superar {NameMaxLength} caracteres.", [member]);
    }

    public static IEnumerable<ValidationResult> ValidateOptionalFields(
        string? legalName, string? email, string? phone, string? website, string? logoUrl)
    {
        if (TooLong(legalName, LegalNameMaxLength))
            yield return new ValidationResult($"legalName no puede superar {LegalNameMaxLength} caracteres.", ["LegalName"]);

        var normalizedEmail = Normalize(email);
        if (normalizedEmail is not null && (normalizedEmail.Length > EmailMaxLength || !EmailAttribute.IsValid(normalizedEmail)))
            yield return new ValidationResult("email no es un correo electrónico válido.", ["Email"]);

        if (TooLong(phone, PhoneMaxLength))
            yield return new ValidationResult($"phone no puede superar {PhoneMaxLength} caracteres.", ["Phone"]);

        if (!IsValidUriOrBlank(website))
            yield return new ValidationResult("website debe ser una URL absoluta http(s).", ["Website"]);

        if (!IsValidUriOrBlank(logoUrl))
            yield return new ValidationResult("logoUrl debe ser una URL absoluta http(s).", ["LogoUrl"]);
    }

    private static bool TooLong(string? value, int maxLength) => Normalize(value)?.Length > maxLength;

    private static bool IsValidUriOrBlank(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null
            || (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
    }
}
