namespace Spot.Booking.Api.Models;

/// <summary>
/// Read-only view of the `businesses` table, owned and migrated by Spot.Business.Api (same
/// excluded-from-migrations pattern as <see cref="UserReference"/>). Only the columns availability
/// (#66) needs are mapped.
/// </summary>
public class BusinessReference
{
    public Guid Id { get; set; }
    public bool IsActive { get; set; }
}
