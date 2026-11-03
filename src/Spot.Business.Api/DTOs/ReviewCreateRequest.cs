using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the ReviewCreateRequest schema in contracts/spot-api.yaml. Has no booking, user or
/// business on purpose: the booking comes from the route, and the user and business from the booking.
/// <see cref="Rating"/> is nullable only so [Required] can tell a missing rating apart from a 0.
/// </summary>
public sealed class ReviewCreateRequest
{
    [Required]
    [Range(1, 5)]
    public int? Rating { get; set; }

    public string? Comment { get; set; }
}
