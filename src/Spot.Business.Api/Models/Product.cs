namespace Spot.Business.Api.Models;

public class Product
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Business Business { get; set; } = null!;
    public ICollection<ProductPhoto> Photos { get; set; } = [];
}
