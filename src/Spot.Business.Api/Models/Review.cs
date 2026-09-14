namespace Spot.Business.Api.Models;

public class Review
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public short Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Business Business { get; set; } = null!;
}
