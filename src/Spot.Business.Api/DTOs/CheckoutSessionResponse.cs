namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "CheckoutSessionResponse" schema: Stripe's hosted payment page.</summary>
public sealed record CheckoutSessionResponse(string CheckoutUrl);
