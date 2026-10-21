namespace Spot.Business.Api.Services;

/// <summary>
/// POST /business/billing/webhooks/stripe (#138): verifies a Stripe event and applies it. The only
/// way a subscription is ever created or activated.
/// </summary>
public interface IStripeWebhookService
{
    /// <param name="json">The request body exactly as received; the signature is over these bytes.</param>
    /// <param name="signatureHeader">The Stripe-Signature header, or null if absent.</param>
    /// <exception cref="Billing.InvalidStripeWebhookException">Missing or invalid signature (400).</exception>
    Task HandleAsync(string json, string? signatureHeader, CancellationToken ct = default);
}
