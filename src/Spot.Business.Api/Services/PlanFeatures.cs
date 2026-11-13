using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Services;

/// <summary>
/// The single place where what each plan includes is defined (#138). Returned by
/// GET /business/billing/plans; <see cref="PlanFeaturesDto.MaxServices"/> is also enforced when a
/// service is created or reactivated (ServiceOfferingService). The photo limits come from the
/// PhotoLimits configuration for now, not from the plan.
/// </summary>
public static class PlanFeatures
{
    public static PlanFeaturesDto For(PlanCode code) => code switch
    {
        PlanCode.FREE => new PlanFeaturesDto(MaxServices: 5, MaxPhotosPerBusiness: 3, MaxPhotosPerService: 3),
        PlanCode.PRO => new PlanFeaturesDto(MaxServices: null, MaxPhotosPerBusiness: null, MaxPhotosPerService: null),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown plan."),
    };

    /// <summary>
    /// The subscription that is actually in effect: <paramref name="current"/> only while its paid
    /// period lasts. One whose period is over counts as FREE (null) even while its row still says
    /// ACTIVE/PAST_DUE (e.g. a missed webhook): it's never trusted past current_period_end.
    /// </summary>
    public static Subscription? InEffect(Subscription? current, DateTimeOffset now) =>
        current is not null && current.CurrentPeriodEnd > now ? current : null;
}
