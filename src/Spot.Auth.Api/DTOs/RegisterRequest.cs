using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.DTOs;

/// <summary>Matches the RegisterRequest schema in contracts/spot-api.yaml.</summary>
public sealed class RegisterRequest : IValidatableObject
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = default!;

    [Required]
    [MinLength(8)]
    [MaxLength(72)]
    public string Password { get; set; } = default!;

    /// <summary>Required only when the (effective) role is CLIENT — see <see cref="Validate"/>.</summary>
    [MaxLength(100)]
    public string? FirstName { get; set; }

    /// <summary>Required only when the (effective) role is CLIENT — see <see cref="Validate"/>.</summary>
    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>
    /// Omitted defaults to CLIENT (see AuthService.RegisterAsync). Only CLIENT and BUSINESS
    /// are ever accepted from the client — SUPERADMIN (or anything else) is rejected by
    /// <see cref="Validate"/> below, since the caller never gets to self-grant an elevated role.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UserRole? Role { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Role is not (null or UserRole.CLIENT or UserRole.BUSINESS))
        {
            yield return new ValidationResult("role debe ser CLIENT o BUSINESS.", [nameof(Role)]);
            yield break;
        }

        // A BUSINESS account is the business itself — no person name (see AuthService.RegisterAsync,
        // which ignores firstName/lastName entirely for BUSINESS). Only CLIENT needs them.
        if (Role is null or UserRole.CLIENT)
        {
            if (string.IsNullOrWhiteSpace(FirstName))
                yield return new ValidationResult("firstName es requerido para cuentas CLIENT.", [nameof(FirstName)]);

            if (string.IsNullOrWhiteSpace(LastName))
                yield return new ValidationResult("lastName es requerido para cuentas CLIENT.", [nameof(LastName)]);
        }
    }
}
