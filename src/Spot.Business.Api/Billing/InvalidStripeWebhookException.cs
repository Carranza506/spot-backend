namespace Spot.Business.Api.Billing;

/// <summary>A webhook request whose Stripe-Signature can't be verified. Mapped to 400 by the controller.</summary>
public sealed class InvalidStripeWebhookException(string message, Exception? inner = null) : Exception(message, inner);
