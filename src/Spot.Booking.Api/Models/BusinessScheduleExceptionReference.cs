namespace Spot.Booking.Api.Models;

/// <summary>Read-only view of `business_schedule_exceptions` (owned by Spot.Business.Api; see <see cref="BusinessReference"/>).</summary>
public class BusinessScheduleExceptionReference
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public DateOnly ExceptionDate { get; set; }
    public bool IsClosed { get; set; }
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }
}
