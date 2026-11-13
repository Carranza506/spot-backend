namespace Spot.Booking.Api.Models;

/// <summary>
/// Read-only view of `business_hours` (owned by Spot.Business.Api; see <see cref="BusinessReference"/>).
/// <see cref="DayOfWeek"/> is 0 = Sunday … 6 = Saturday, same numbering as .NET's DayOfWeek.
/// </summary>
public class BusinessHoursReference
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public short DayOfWeek { get; set; }
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }
    public bool IsClosed { get; set; }
}
