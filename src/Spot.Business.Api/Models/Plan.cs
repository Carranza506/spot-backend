namespace Spot.Business.Api.Models;

/// <summary>A plan a business can be on (#138). What each plan includes is defined in code, see Services/PlanFeatures.cs.</summary>
public class Plan
{
    public Guid Id { get; set; }
    public PlanCode Code { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>Price per <see cref="Interval"/> in colones (CRC), NUMERIC(12,2).</summary>
    public decimal PriceCrc { get; set; }

    public BillingInterval Interval { get; set; }

    /// <summary>
    /// The Stripe Price to subscribe to. Null for FREE, and seeded null for PRO too: PRO's price
    /// comes from Stripe:ProPriceId unless this is set (see BillingService).
    /// </summary>
    public string? StripePriceId { get; set; }

    public bool IsActive { get; set; } = true;
}
