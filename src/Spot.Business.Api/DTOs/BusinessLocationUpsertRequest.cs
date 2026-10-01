using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the BusinessLocationUpsertRequest schema in contracts/spot-api.yaml. Text fields are
/// validated as they'll be stored — trimmed, blank optional fields become null (see
/// <see cref="BusinessFieldRules.Normalize"/>); a missing or blank <see cref="Country"/> falls
/// back to the contract's default, <see cref="DefaultCountry"/>.
/// </summary>
public sealed class BusinessLocationUpsertRequest : IValidatableObject
{
    public const string DefaultCountry = "Costa Rica";
    public const int CityMaxLength = 100;
    public const int ProvinceMaxLength = 100;
    public const int CountryMaxLength = 100;
    public const int PostalCodeMaxLength = 20;

    [Required]
    public string Address { get; set; } = default!;

    public string? City { get; set; }

    public string? Province { get; set; }

    public string? Country { get; set; }

    public string? PostalCode { get; set; }

    [Required]
    public GeoPointRequest Location { get; set; } = default!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Address))
            yield return new ValidationResult("address no puede estar vacío ni contener solo espacios.", [nameof(Address)]);

        if (TooLong(City, CityMaxLength))
            yield return new ValidationResult($"city no puede superar {CityMaxLength} caracteres.", [nameof(City)]);

        if (TooLong(Province, ProvinceMaxLength))
            yield return new ValidationResult($"province no puede superar {ProvinceMaxLength} caracteres.", [nameof(Province)]);

        if (TooLong(Country, CountryMaxLength))
            yield return new ValidationResult($"country no puede superar {CountryMaxLength} caracteres.", [nameof(Country)]);

        if (TooLong(PostalCode, PostalCodeMaxLength))
            yield return new ValidationResult($"postalCode no puede superar {PostalCodeMaxLength} caracteres.", [nameof(PostalCode)]);
    }

    private static bool TooLong(string? value, int maxLength) => BusinessFieldRules.Normalize(value)?.Length > maxLength;
}
