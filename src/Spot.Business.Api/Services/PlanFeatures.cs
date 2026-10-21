using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Services;

/// <summary>
/// The single place where what each plan includes is defined (#138). Returned by
/// GET /business/billing/plans as information only: enforcing these limits is a separate story.
/// </summary>
public static class PlanFeatures
{
    public static PlanFeaturesDto For(PlanCode code) => code switch
    {
        PlanCode.FREE => new PlanFeaturesDto(MaxServices: 5, MaxPhotosPerBusiness: 3, MaxPhotosPerService: 3),
        PlanCode.PRO => new PlanFeaturesDto(MaxServices: null, MaxPhotosPerBusiness: null, MaxPhotosPerService: null),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown plan."),
    };
}
