namespace Spot.Business.Api.Models;

public class FavoriteBusiness
{
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Business Business { get; set; } = null!;
}
