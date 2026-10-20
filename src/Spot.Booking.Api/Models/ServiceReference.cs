namespace Spot.Booking.Api.Models;

/// <summary>Read-only view of `services` (owned by Spot.Business.Api; see <see cref="BusinessReference"/>).</summary>
public class ServiceReference
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; }
}
