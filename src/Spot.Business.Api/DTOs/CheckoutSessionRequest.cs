using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "CheckoutSessionRequest" schema. <see cref="PlanCode"/> stays a string here: an
/// unknown code is a 400 with a specific message from BillingService, not a model-binding failure.
/// </summary>
public sealed class CheckoutSessionRequest
{
    [Required]
    public string PlanCode { get; set; } = default!;
}
