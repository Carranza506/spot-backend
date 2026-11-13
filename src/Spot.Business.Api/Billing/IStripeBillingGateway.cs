using Spot.Business.Api.Models;

namespace Spot.Business.Api.Billing;

/// <summary>What Spot asks Stripe Checkout for: one plan, for one business account.</summary>
/// <param name="CustomerId">The account's existing Stripe customer, if it ever had one; otherwise Checkout creates one.</param>
public sealed record StripeCheckoutRequest(Guid AccountId, PlanCode PlanCode, string PriceId, string? CustomerId);

/// <summary>A Stripe subscription, read through the API (the client's pinned API version), never from an event payload.</summary>
/// <param name="Status">Stripe's own status ("active", "past_due", ...); see <see cref="StripeSubscriptionMapping.MapStatus"/>.</param>
/// <param name="CancelAtPeriodEnd">True when the subscription won't renew (cancel_at_period_end, or a cancel_at date).</param>
/// <param name="AccountId">metadata.accountId, set by Spot's Checkout session; null for subscriptions Spot didn't create.</param>
public sealed record StripeSubscriptionSnapshot(
    string Id,
    string CustomerId,
    string Status,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    string? PriceId,
    Guid? AccountId);

/// <param name="Mode">"subscription", "payment" or "setup".</param>
public sealed record StripeCheckoutSessionSnapshot(string Id, string Mode, string? SubscriptionId, string? ClientReferenceId);

/// <param name="AmountPaid">Minor units (céntimos for CRC).</param>
/// <param name="AmountDue">Minor units (céntimos for CRC).</param>
public sealed record StripeInvoiceSnapshot(
    string Id,
    string? SubscriptionId,
    string Currency,
    long AmountPaid,
    long AmountDue,
    DateTimeOffset? PaidAt);

/// <summary>A webhook event whose signature was verified. <see cref="ObjectId"/> is data.object.id.</summary>
public sealed record StripeWebhookEvent(string Id, string Type, string? ObjectId);

/// <summary>
/// Every call Spot makes to Stripe (#138), behind an interface so the billing logic can be tested
/// without the network. Implementations return Spot's own records, never Stripe.net types.
/// </summary>
public interface IStripeBillingGateway
{
    /// <summary>Creates a Checkout session in subscription mode and returns its hosted page URL.</summary>
    Task<string> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken ct = default);

    Task<StripeCheckoutSessionSnapshot> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default);

    Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Sets the subscription to end at the end of the current period instead of renewing.</summary>
    Task<StripeSubscriptionSnapshot> CancelAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default);

    Task<StripeInvoiceSnapshot> GetInvoiceAsync(string invoiceId, CancellationToken ct = default);

    /// <summary>
    /// Verifies <paramref name="signatureHeader"/> (Stripe-Signature) against the exact raw
    /// <paramref name="json"/> body and <paramref name="secret"/>, and parses the event. The event's
    /// API version isn't required to match the client's: only its type and object id are used.
    /// </summary>
    /// <exception cref="InvalidStripeWebhookException">Missing, invalid or expired signature, or not an event.</exception>
    StripeWebhookEvent VerifyWebhook(string json, string signatureHeader, string secret);
}
