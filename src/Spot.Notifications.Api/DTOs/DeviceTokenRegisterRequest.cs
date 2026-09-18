using System.ComponentModel.DataAnnotations;

namespace Spot.Notifications.Api.DTOs;

/// <summary>
/// Matches the DeviceTokenRegisterRequest schema in contracts/spot-api.yaml.
/// <see cref="Platform"/> is a plain string (not the <see cref="Models.DevicePlatform"/> enum
/// directly): the contract represents it as a string enum on the wire, and this service has no
/// global <c>JsonStringEnumConverter</c> registered, so binding it as the C# enum type would
/// require a JSON integer instead of "ANDROID"/"IOS"/"WEB". The controller parses and validates
/// it explicitly, same pattern as DevController's role parsing in Spot.Auth.Api.
/// </summary>
public sealed class DeviceTokenRegisterRequest : IValidatableObject
{
    [Required]
    public string Token { get; set; } = default!;

    [Required]
    public string Platform { get; set; } = default!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already rejects null/"" — this catches the "   " case it doesn't.
        if (!string.IsNullOrEmpty(Token) && string.IsNullOrWhiteSpace(Token))
            yield return new ValidationResult("token no puede estar vacío ni contener solo espacios.", [nameof(Token)]);
    }
}
