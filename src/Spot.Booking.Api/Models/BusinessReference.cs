namespace Spot.Booking.Api.Models;

/// <summary>
/// Read-only view of the `businesses` table, owned and migrated by Spot.Business.Api (same
/// excluded-from-migrations pattern as <see cref="UserReference"/>). Only the columns availability
/// (#66) and the bookings ownership checks (#68) need are mapped.
/// </summary>
public class BusinessReference
{
    public Guid Id { get; set; }

    /// <summary>The BUSINESS account (users.id) this business is — the owner allowed to read its bookings.</summary>
    public Guid AccountId { get; set; }

    public bool IsActive { get; set; }
}
