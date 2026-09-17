using System.ComponentModel.DataAnnotations;

namespace Spot.Auth.Api.DTOs;

/// <summary>Matches the RegisterRequest schema in contracts/spot-api.yaml.</summary>
public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = default!;

    [Required]
    [MinLength(8)]
    [MaxLength(72)]
    public string Password { get; set; } = default!;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = default!;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = default!;

    [MaxLength(30)]
    public string? Phone { get; set; }
}
