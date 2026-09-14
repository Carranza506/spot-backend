namespace Spot.Business.Api.Models;

public class BusinessOwner
{
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Business Business { get; set; } = null!;
}
