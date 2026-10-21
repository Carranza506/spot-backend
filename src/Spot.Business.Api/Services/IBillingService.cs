using Spot.Business.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

/// <summary>
/// Plans and the caller's subscription (contracts/spot-api.yaml, "BUSINESS - Billing", #138).
/// Nothing here ever activates a subscription: only <see cref="IStripeWebhookService"/> does, from a
/// verified Stripe event.
/// </summary>
public interface IBillingService
{
    /// <summary>Active plans, cheapest first, with their (informational) features.</summary>
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// The account's effective plan: its ACTIVE/PAST_DUE subscription while current_period_end
    /// hasn't passed; FREE otherwise (no subscription, canceled, expired, or period over).
    /// </summary>
    Task<SubscriptionDto> GetMySubscriptionAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Creates a Stripe Checkout session for <paramref name="planCode"/>.</summary>
    /// <exception cref="InvalidPlanException">Unknown, inactive or FREE plan.</exception>
    /// <exception cref="AlreadySubscribedException">The account already has an ACTIVE/PAST_DUE subscription.</exception>
    Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(Guid accountId, string planCode, CancellationToken ct = default);

    /// <summary>
    /// Sets the account's ACTIVE/PAST_DUE subscription to cancel at the end of the period.
    /// Idempotent. Null if there's nothing to cancel (404).
    /// </summary>
    Task<SubscriptionDto?> CancelMySubscriptionAsync(Guid accountId, CancellationToken ct = default);

    Task<PaginatedResponse<SubscriptionPaymentDto>> ListMyPaymentsAsync(
        Guid accountId, int page, int pageSize, CancellationToken ct = default);
}
