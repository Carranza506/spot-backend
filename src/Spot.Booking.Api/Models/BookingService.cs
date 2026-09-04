namespace Spot.Booking.Api.Models;

public class BookingService
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int DurationMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Booking Booking { get; set; } = null!;
}
