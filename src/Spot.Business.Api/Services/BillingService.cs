using Microsoft.Extensions.Options;
using Spot.Business.Api.Billing;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Options;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public sealed class BillingService(
    IBillingRepository repository,
    IStripeBillingGateway stripe,
    IOptions<StripeOptions> stripeOptions,
    TimeProvider timeProvider,
    ILogger<BillingService> logger) : IBillingService
{
    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = await repository.ListActivePlansAsync(ct);
        return plans.Select(PlanDto.FromEntity).ToList();
    }

    public async Task<SubscriptionDto> GetMySubscriptionAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = await repository.GetCurrentSubscriptionAsync(accountId, ct);

        // A subscription whose paid period is over counts as FREE even while its row still says
        // ACTIVE/PAST_DUE (e.g. a missed webhook): it's never trusted past current_period_end.
        if (current is not null && current.CurrentPeriodEnd > timeProvider.GetUtcNow())
            return SubscriptionDto.FromEntity(current);

        return SubscriptionDto.Free(await GetFreePlanAsync(ct));
    }

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(
        Guid accountId, string planCode, CancellationToken ct = default)
    {
        // Exact, case-sensitive names only: Enum.TryParse alone would also take "pro" or "1".
        if (!Enum.GetNames<PlanCode>().Contains(planCode) || Enum.Parse<PlanCode>(planCode) == PlanCode.FREE)
            throw new InvalidPlanException(planCode);

        var plan = await repository.GetPlanByCodeAsync(Enum.Parse<PlanCode>(planCode), ct);
        if (plan is not { IsActive: true })
            throw new InvalidPlanException(planCode);

        // By status alone, not current_period_end: ux_subscriptions_one_current is on status, so a
        // second paid subscription would be rejected by the webhook after the user already paid.
        if (await repository.GetCurrentSubscriptionAsync(accountId, ct) is not null)
            throw new AlreadySubscribedException(accountId);

        // Resolving the price reads the Stripe options, so an unconfigured Stripe fails here (500)
        // with the validator's message in the log.
        var priceId = stripeOptions.Value.PriceIdFor(plan)
            ?? throw new InvalidOperationException($"No Stripe price is configured for plan {plan.Code}.");

        var customerId = await repository.GetLatestStripeCustomerIdAsync(accountId, ct);
        var url = await stripe.CreateCheckoutSessionAsync(
            new StripeCheckoutRequest(accountId, plan.Code, priceId, customerId), ct);

        logger.LogInformation("Checkout session created for account {AccountId}, plan {PlanCode}.", accountId, plan.Code);
        return new CheckoutSessionResponse(url);
    }

    public async Task<SubscriptionDto?> CancelMySubscriptionAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = await repository.GetCurrentSubscriptionAsync(accountId, ct);
        if (current is null)
            return null;

        // Idempotent: already set to cancel, nothing to ask Stripe.
        if (!current.CancelAtPeriodEnd)
        {
            var snapshot = await stripe.CancelAtPeriodEndAsync(current.StripeSubscriptionId, ct);

            // Stripe's answer is the source of truth; the customer.subscription.updated webhook that
            // follows applies the same values again, with no further effect.
            var status = StripeSubscriptionMapping.MapStatus(snapshot.Status) ?? current.Status;
            if (StripeSubscriptionMapping.Apply(current, snapshot, status, plan: null))
                await repository.SaveChangesAsync(ct);

            logger.LogInformation(
                "Subscription {SubscriptionId} of account {AccountId} set to cancel at period end.", current.Id, accountId);
        }

        return SubscriptionDto.FromEntity(current);
    }

    public async Task<PaginatedResponse<SubscriptionPaymentDto>> ListMyPaymentsAsync(
        Guid accountId, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await repository.ListPaymentsAsync(accountId, page, pageSize, ct);
        var dtos = items.Select(SubscriptionPaymentDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new PaginatedResponse<SubscriptionPaymentDto>(dtos, new PaginationMeta(page, pageSize, total, totalPages));
    }

    private async Task<Plan> GetFreePlanAsync(CancellationToken ct) =>
        await repository.GetPlanByCodeAsync(PlanCode.FREE, ct)
        ?? throw new InvalidOperationException("The FREE plan is missing from the plans table (seeded by the AddBillingSubscriptions migration).");
}
