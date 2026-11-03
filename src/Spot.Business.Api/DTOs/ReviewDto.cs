using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "Review" schema in contracts/spot-api.yaml.</summary>
public sealed record ReviewDto(
    Guid Id,
    Guid BookingId,
    Guid UserId,
    Guid BusinessId,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ReviewDto FromEntity(Review review) => new(
        Id: review.Id,
        BookingId: review.BookingId,
        UserId: review.UserId,
        BusinessId: review.BusinessId,
        Rating: review.Rating,
        Comment: review.Comment,
        CreatedAt: review.CreatedAt,
        UpdatedAt: review.UpdatedAt);
}
