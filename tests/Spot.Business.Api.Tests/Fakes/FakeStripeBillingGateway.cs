using Spot.Business.Api.Billing;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IStripeBillingGateway — no network. Tests put the objects "Stripe" should
/// return in the dictionaries, and inspect the calls Spot made.
/// </summary>
public sealed class FakeStripeBillingGateway : IStripeBillingGateway
{
    /// <summary>The only Stripe-Signature value <see cref="VerifyWebhook"/> accepts.</summary>
    public const string ValidSignature = "t=1,v1=valid";

    public Dictionary<string, StripeSubscriptionSnapshot> Subscriptions { get; } = [];
    public Dictionary<string, StripeCheckoutSessionSnapshot> Sessions { get; } = [];
    public Dictionary<string, StripeInvoiceSnapshot> Invoices { get; } = [];

    public List<StripeCheckoutRequest> CheckoutRequests { get; } = [];
    public List<string> CancelRequests { get; } = [];

    /// <summary>What <see cref="VerifyWebhook"/> returns for a valid signature.</summary>
    public StripeWebhookEvent? NextEvent { get; set; }

    public void Reset()
    {
        Subscriptions.Clear();
        Sessions.Clear();
        Invoices.Clear();
        CheckoutRequests.Clear();
        CancelRequests.Clear();
        NextEvent = null;
    }

    public Task<string> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken ct = default)
    {
        CheckoutRequests.Add(request);
        return Task.FromResult("https://checkout.stripe.com/c/pay/cs_test_fake");
    }

    public Task<StripeCheckoutSessionSnapshot> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult(Sessions[sessionId]);

    public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default) =>
        Task.FromResult(Subscriptions[subscriptionId]);

    public Task<StripeSubscriptionSnapshot> CancelAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default)
    {
        CancelRequests.Add(subscriptionId);
        var updated = Subscriptions[subscriptionId] with { CancelAtPeriodEnd = true };
        Subscriptions[subscriptionId] = updated;
        return Task.FromResult(updated);
    }

    public Task<StripeInvoiceSnapshot> GetInvoiceAsync(string invoiceId, CancellationToken ct = default) =>
        Task.FromResult(Invoices[invoiceId]);

    public StripeWebhookEvent VerifyWebhook(string json, string signatureHeader, string secret) =>
        signatureHeader == ValidSignature && NextEvent is not null
            ? NextEvent
            : throw new InvalidStripeWebhookException("Invalid signature (fake).");
}
