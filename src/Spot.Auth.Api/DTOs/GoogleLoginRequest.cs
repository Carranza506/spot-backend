using System.ComponentModel.DataAnnotations;

namespace Spot.Auth.Api.DTOs;

/// <summary>Matches the GoogleLoginRequest schema in contracts/spot-api.yaml.</summary>
public sealed class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = default!;
}
