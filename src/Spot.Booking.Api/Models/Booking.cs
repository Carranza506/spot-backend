namespace Spot.Booking.Api.Models;

public class Booking
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ServiceId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.PENDING;
    public string ServiceName { get; set; } = null!;
    public decimal ServicePrice { get; set; }
    public int ServiceDurationMinutes { get; set; }
    public string? Notes { get; set; }

    /// <summary>Why the booking was cancelled (#163): only set by a cancel that gave a reason.</summary>
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
