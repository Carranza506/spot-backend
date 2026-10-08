using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "FavoriteBusiness" schema in contracts/spot-api.yaml: when the caller saved the
/// business, plus the business itself in the same shape as GET /business/businesses/{businessId}.
/// </summary>
public sealed record FavoriteBusinessDto(Guid BusinessId, DateTimeOffset CreatedAt, BusinessDto Business)
{
    /// <summary>Expects <see cref="FavoriteBusiness.Business"/> to be loaded.</summary>
    public static FavoriteBusinessDto FromEntity(FavoriteBusiness favorite) => new(
        BusinessId: favorite.BusinessId,
        CreatedAt: favorite.CreatedAt,
        Business: BusinessDto.FromEntity(favorite.Business));
}
