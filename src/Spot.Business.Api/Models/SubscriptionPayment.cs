namespace Spot.Business.Api.Models;

/// <summary>One Stripe invoice of a <see cref="Subscription"/> (#138), paid or failed. One row per invoice.</summary>
public class SubscriptionPayment
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }

    /// <summary>Colones (CRC), NUMERIC(12,2): Stripe's minor units divided by 100 (CRC is a two-decimal currency there).</summary>
    public decimal AmountCrc { get; set; }

    public SubscriptionPaymentStatus Status { get; set; }

    /// <summary>The invoice id; unique, so the same invoice event received twice is recorded once.</summary>
    public string StripeInvoiceId { get; set; } = null!;

    /// <summary>When Stripe marked the invoice paid. Null while <see cref="Status"/> is FAILED.</summary>
    public DateTimeOffset? PaidAt { get; set; }

    public Subscription Subscription { get; set; } = null!;
}
