namespace Spot.Business.Api.Models;

/// <summary>
/// A business account's Stripe subscription (#138). Only ever created or changed from a verified
/// Stripe webhook (or Stripe's answer to a cancel request), never from the Checkout redirect.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; }

    /// <summary>The BUSINESS `users` row that pays. Owned by Spot.Auth.Api; see <see cref="UserReference"/>.</summary>
    public Guid AccountId { get; set; }

    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public string StripeCustomerId { get; set; } = null!;
    public string StripeSubscriptionId { get; set; } = null!;
    public DateTimeOffset CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Plan Plan { get; set; } = null!;
    public ICollection<SubscriptionPayment> Payments { get; set; } = [];
}
