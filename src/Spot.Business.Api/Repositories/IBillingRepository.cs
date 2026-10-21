using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

/// <summary>Outcome of <see cref="IBillingRepository.AddSubscriptionAsync"/>.</summary>
public enum SubscriptionInsertResult
{
    Added,

    /// <summary>A row with the same stripe_subscription_id already exists (the same event processed concurrently).</summary>
    DuplicateStripeSubscription,

    /// <summary>The account already has an ACTIVE/PAST_DUE subscription (ux_subscriptions_one_current).</summary>
    AccountAlreadySubscribed,
}

/// <summary>Persistence for plans, subscriptions and subscription_payments (#138).</summary>
public interface IBillingRepository
{
    /// <summary>Active plans, cheapest first (then by code). Not tracked.</summary>
    Task<IReadOnlyList<Plan>> ListActivePlansAsync(CancellationToken ct = default);

    /// <summary>Every plan, active or not — to match a Stripe price back to its plan. Not tracked.</summary>
    Task<IReadOnlyList<Plan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>The plan with <paramref name="code"/>, active or not. Not tracked.</summary>
    Task<Plan?> GetPlanByCodeAsync(PlanCode code, CancellationToken ct = default);

    /// <summary>
    /// The account's ACTIVE/PAST_DUE subscription (at most one, by ux_subscriptions_one_current),
    /// with <see cref="Subscription.Plan"/> loaded, whatever its current_period_end. Tracked.
    /// </summary>
    Task<Subscription?> GetCurrentSubscriptionAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>The Stripe customer of the account's most recent subscription, if it ever had one.</summary>
    Task<string?> GetLatestStripeCustomerIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>The subscription with <paramref name="stripeSubscriptionId"/>, with its plan loaded. Tracked.</summary>
    Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Inserts <paramref name="subscription"/>. A unique violation is reported in the result
    /// instead of thrown, and the entity is detached again so later saves don't retry it.
    /// </summary>
    Task<SubscriptionInsertResult> AddSubscriptionAsync(Subscription subscription, CancellationToken ct = default);

    /// <summary>Persists changes made to tracked subscriptions or payments.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>The payment of <paramref name="stripeInvoiceId"/>, if already recorded. Tracked.</summary>
    Task<SubscriptionPayment?> GetPaymentByInvoiceIdAsync(string stripeInvoiceId, CancellationToken ct = default);

    /// <summary>
    /// Inserts <paramref name="payment"/>. False (and the entity detached again) if the invoice was
    /// recorded concurrently (unique stripe_invoice_id).
    /// </summary>
    Task<bool> AddPaymentAsync(SubscriptionPayment payment, CancellationToken ct = default);

    /// <summary>
    /// A page of the payments of all of the account's subscriptions: paid ones first, most recent
    /// first, then failed ones (no paid_at), then by id so pages are stable. Not tracked.
    /// </summary>
    Task<(IReadOnlyList<SubscriptionPayment> Items, int Total)> ListPaymentsAsync(
        Guid accountId, int page, int pageSize, CancellationToken ct = default);
}
