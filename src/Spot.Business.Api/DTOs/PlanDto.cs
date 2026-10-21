using Spot.Business.Api.Models;
using Spot.Business.Api.Services;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "Plan" schema in contracts/spot-api.yaml. Never exposes the Stripe price id.</summary>
public sealed record PlanDto(string Code, string Name, decimal PriceCrc, string Interval, PlanFeaturesDto Features)
{
    public static PlanDto FromEntity(Plan plan) => new(
        plan.Code.ToString(), plan.Name, plan.PriceCrc, plan.Interval.ToString(), PlanFeatures.For(plan.Code));
}

/// <summary>
/// Matches the "PlanFeatures" schema. Informational only for now (#138): nothing enforces these
/// limits yet. Null means the plan sets no limit; it's written as an explicit null, not omitted.
/// </summary>
public sealed record PlanFeaturesDto(int? MaxServices, int? MaxPhotosPerBusiness, int? MaxPhotosPerService);
