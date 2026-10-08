using System.ComponentModel.DataAnnotations;

namespace Spot.Auth.Api.DTOs;

/// <summary>
/// Matches the ChangePasswordRequest schema in contracts/spot-api.yaml. CurrentPassword has no
/// length constraints — like LoginRequest.Password, it's only compared against the stored hash,
/// never validated for "strength".
/// </summary>
public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = default!;

    [Required]
    [MinLength(8)]
    [MaxLength(72)]
    public string NewPassword { get; set; } = default!;
}
