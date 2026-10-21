using Spot.Business.Api.Models;

namespace Spot.Business.Api.Billing;

/// <summary>The webhook event types Spot handles (#138). Any other type is acknowledged and ignored.</summary>
public static class StripeEventTypes
{
    public const string CheckoutSessionCompleted = "checkout.session.completed";
    public const string InvoicePaid = "invoice.paid";
    public const string InvoicePaymentFailed = "invoice.payment_failed";
    public const string CustomerSubscriptionUpdated = "customer.subscription.updated";
    public const string CustomerSubscriptionDeleted = "customer.subscription.deleted";
}

/// <summary>How a Stripe subscription becomes a <see cref="Subscription"/> row (#138).</summary>
public static class StripeSubscriptionMapping
{
    /// <summary>
    /// Stripe's subscription status as Spot's. Null for <c>incomplete</c> (the first payment hasn't
    /// gone through yet): there's nothing to store until Stripe moves it on.
    /// </summary>
    /// <remarks>
    /// <c>unpaid</c> (retries exhausted, if the account is set to keep the subscription) stays
    /// PAST_DUE; <c>paused</c> (a trial ended without a payment method) is treated as EXPIRED.
    /// An unknown future status is also null, so it never grants or removes a plan by accident.
    /// </remarks>
    public static SubscriptionStatus? MapStatus(string stripeStatus) => stripeStatus switch
    {
        "active" or "trialing" => SubscriptionStatus.ACTIVE,
        "past_due" or "unpaid" => SubscriptionStatus.PAST_DUE,
        "canceled" => SubscriptionStatus.CANCELED,
        "incomplete_expired" or "paused" => SubscriptionStatus.EXPIRED,
        _ => null,
    };

    /// <summary>
    /// Copies <paramref name="snapshot"/> onto <paramref name="subscription"/>. <paramref name="plan"/>
    /// is only applied when the snapshot's price matched one. Returns whether anything changed.
    /// </summary>
    public static bool Apply(Subscription subscription, StripeSubscriptionSnapshot snapshot, SubscriptionStatus status, Plan? plan)
    {
        var changed = false;

        if (subscription.Status != status)
        {
            subscription.Status = status;
            changed = true;
        }

        if (subscription.CurrentPeriodEnd != snapshot.CurrentPeriodEnd)
        {
            subscription.CurrentPeriodEnd = snapshot.CurrentPeriodEnd;
            changed = true;
        }

        if (subscription.CancelAtPeriodEnd != snapshot.CancelAtPeriodEnd)
        {
            subscription.CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd;
            changed = true;
        }

        if (subscription.StripeCustomerId != snapshot.CustomerId)
        {
            subscription.StripeCustomerId = snapshot.CustomerId;
            changed = true;
        }

        if (plan is not null && subscription.PlanId != plan.Id)
        {
            subscription.PlanId = plan.Id;
            subscription.Plan = plan;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// A Stripe amount in CRC minor units as colones. Stripe treats CRC as a two-decimal currency
    /// (https://docs.stripe.com/currencies), so 1000000 is ₡10,000.00.
    /// </summary>
    public static decimal CrcFromMinorUnits(long amount) => amount / 100m;
}
