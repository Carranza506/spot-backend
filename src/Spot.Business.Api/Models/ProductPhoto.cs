namespace Spot.Business.Api.Models;

public class ProductPhoto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string StorageKey { get; set; } = null!;
    public string? Url { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Product Product { get; set; } = null!;
}
