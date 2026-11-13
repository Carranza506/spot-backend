namespace Spot.Notifications.Api.Models;

/// <summary>
/// Read-only view of the `businesses` table, owned and migrated by Spot.Business.Api (same
/// excluded-from-migrations pattern as <see cref="UserReference"/>). Only what's needed to find the
/// business account a cancelled booking's notification goes to (#79).
/// </summary>
public class BusinessReference
{
    public Guid Id { get; set; }

    /// <summary>The BUSINESS account (users.id) this business is.</summary>
    public Guid AccountId { get; set; }
}
