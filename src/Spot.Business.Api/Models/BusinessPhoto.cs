namespace Spot.Business.Api.Models;

public class BusinessPhoto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string StorageKey { get; set; } = null!;
    public string? Url { get; set; }
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Business Business { get; set; } = null!;
}
