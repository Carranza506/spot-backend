using System.ComponentModel.DataAnnotations;

namespace Spot.Auth.Api.DTOs;

/// <summary>
/// Matches the LoginRequest schema in contracts/spot-api.yaml. Unlike RegisterRequest, the
/// contract puts no length constraints on the password here — this only checks credentials
/// against the stored hash, it never validates password "strength".
/// </summary>
public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;
}
