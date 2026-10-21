namespace Spot.Business.Api.Services;

/// <summary>The requested plan can't be bought: unknown, inactive, or FREE. Mapped to 400 by the controller.</summary>
public sealed class InvalidPlanException(string planCode)
    : Exception($"Plan '{planCode}' can't be subscribed to.")
{
    public string PlanCode { get; } = planCode;
}
