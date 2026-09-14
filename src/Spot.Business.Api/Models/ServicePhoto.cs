namespace Spot.Business.Api.Models;

public class ServicePhoto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string StorageKey { get; set; } = null!;
    public string? Url { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Service Service { get; set; } = null!;
}
