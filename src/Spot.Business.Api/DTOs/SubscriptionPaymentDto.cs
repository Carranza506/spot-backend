using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "SubscriptionPayment" schema. Never exposes the Stripe invoice id.</summary>
public sealed record SubscriptionPaymentDto(
    Guid Id,
    Guid SubscriptionId,
    decimal AmountCrc,
    string Status,
    DateTimeOffset? PaidAt)
{
    public static SubscriptionPaymentDto FromEntity(SubscriptionPayment payment) => new(
        payment.Id, payment.SubscriptionId, payment.AmountCrc, payment.Status.ToString(), payment.PaidAt);
}
