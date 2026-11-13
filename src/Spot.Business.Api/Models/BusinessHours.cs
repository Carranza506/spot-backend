namespace Spot.Business.Api.Models;

public class BusinessHours
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public short DayOfWeek { get; set; }
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Business Business { get; set; } = null!;
}
