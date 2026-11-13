using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "Subscription" schema in contracts/spot-api.yaml: the caller's effective plan. On
/// FREE without a subscription, <see cref="Id"/>, <see cref="Status"/> and
/// <see cref="CurrentPeriodEnd"/> are null (written as null, not omitted).
/// </summary>
public sealed record SubscriptionDto(
    Guid? Id,
    PlanDto Plan,
    string? Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd)
{
    public static SubscriptionDto FromEntity(Subscription subscription) => new(
        subscription.Id,
        PlanDto.FromEntity(subscription.Plan),
        subscription.Status.ToString(),
        subscription.CurrentPeriodEnd,
        subscription.CancelAtPeriodEnd);

    public static SubscriptionDto Free(Plan freePlan) => new(null, PlanDto.FromEntity(freePlan), null, null, false);
}
