namespace Spot.Business.Api.Models;

public class BusinessCategory
{
    public Guid BusinessId { get; set; }
    public Guid CategoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Business Business { get; set; } = null!;
    public Category Category { get; set; } = null!;
}
