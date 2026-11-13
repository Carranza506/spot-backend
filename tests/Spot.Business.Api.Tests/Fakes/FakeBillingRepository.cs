using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IBillingRepository — no database involved. Seeded with the same FREE and
/// PRO plans as the migration, and enforces the same unique rules (stripe_subscription_id,
/// ux_subscriptions_one_current, stripe_invoice_id), so idempotency can be tested without Postgres.
/// </summary>
public sealed class FakeBillingRepository : IBillingRepository
{
    private readonly List<Plan> _plans = [];
    private readonly List<Subscription> _subscriptions = [];
    private readonly List<SubscriptionPayment> _payments = [];

    public FakeBillingRepository() => Reset();

    public IReadOnlyList<Plan> Plans => _plans;
    public IReadOnlyList<Subscription> Subscriptions => _subscriptions;
    public IReadOnlyList<SubscriptionPayment> Payments => _payments;

    /// <summary>How many times changes were persisted — to assert a duplicate event wrote nothing.</summary>
    public int SaveCount { get; private set; }

    public Plan FreePlan => _plans.Single(p => p.Code == PlanCode.FREE);
    public Plan ProPlan => _plans.Single(p => p.Code == PlanCode.PRO);

    public void Reset()
    {
        _plans.Clear();
        _subscriptions.Clear();
        _payments.Clear();
        SaveCount = 0;
        _plans.Add(new Plan
        {
            Id = BusinessDbContext.FreePlanId, Code = PlanCode.FREE, Name = "Gratis", PriceCrc = 0m,
            Interval = BillingInterval.MONTHLY, IsActive = true,
        });
        _plans.Add(new Plan
        {
            Id = BusinessDbContext.ProPlanId, Code = PlanCode.PRO, Name = "Pro", PriceCrc = 10000m,
            Interval = BillingInterval.MONTHLY, IsActive = true,
        });
    }

    /// <summary>Seeds a subscription directly, bypassing AddSubscriptionAsync — for test setup.</summary>
    public Subscription Seed(Subscription subscription)
    {
        if (subscription.Id == Guid.Empty)
            subscription.Id = Guid.NewGuid();
        if (subscription.CreatedAt == default)
            subscription.CreatedAt = DateTimeOffset.UtcNow;

        subscription.Plan = _plans.Single(p => p.Id == subscription.PlanId);
        _subscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>Seeds a payment directly, for test setup. Its subscription must already be seeded.</summary>
    public SubscriptionPayment SeedPayment(SubscriptionPayment payment)
    {
        if (payment.Id == Guid.Empty)
            payment.Id = Guid.NewGuid();

        payment.Subscription = _subscriptions.Single(s => s.Id == payment.SubscriptionId);
        _payments.Add(payment);
        return payment;
    }

    public Task<IReadOnlyList<Plan>> ListActivePlansAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Plan>>(
            _plans.Where(p => p.IsActive).OrderBy(p => p.PriceCrc).ThenBy(p => p.Code).ToList());

    public Task<IReadOnlyList<Plan>> ListPlansAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Plan>>(_plans.ToList());

    public Task<Plan?> GetPlanByCodeAsync(PlanCode code, CancellationToken ct = default) =>
        Task.FromResult(_plans.FirstOrDefault(p => p.Code == code));

    public Task<Subscription?> GetCurrentSubscriptionAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_subscriptions.FirstOrDefault(s => s.AccountId == accountId && IsCurrent(s.Status)));

    public Task<string?> GetLatestStripeCustomerIdAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_subscriptions
            .Where(s => s.AccountId == accountId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.StripeCustomerId)
            .FirstOrDefault());

    public Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId, CancellationToken ct = default) =>
        Task.FromResult(_subscriptions.FirstOrDefault(s => s.StripeSubscriptionId == stripeSubscriptionId));

    public Task<SubscriptionInsertResult> AddSubscriptionAsync(Subscription subscription, CancellationToken ct = default)
    {
        if (_subscriptions.Any(s => s.StripeSubscriptionId == subscription.StripeSubscriptionId))
            return Task.FromResult(SubscriptionInsertResult.DuplicateStripeSubscription);
        if (IsCurrent(subscription.Status)
            && _subscriptions.Any(s => s.AccountId == subscription.AccountId && IsCurrent(s.Status)))
            return Task.FromResult(SubscriptionInsertResult.AccountAlreadySubscribed);

        subscription.Id = Guid.NewGuid();
        subscription.CreatedAt = DateTimeOffset.UtcNow;
        subscription.UpdatedAt = subscription.CreatedAt;
        _subscriptions.Add(subscription);
        SaveCount++;
        return Task.FromResult(SubscriptionInsertResult.Added);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<SubscriptionPayment?> GetPaymentByInvoiceIdAsync(string stripeInvoiceId, CancellationToken ct = default) =>
        Task.FromResult(_payments.FirstOrDefault(p => p.StripeInvoiceId == stripeInvoiceId));

    public Task<bool> AddPaymentAsync(SubscriptionPayment payment, CancellationToken ct = default)
    {
        if (_payments.Any(p => p.StripeInvoiceId == payment.StripeInvoiceId))
            return Task.FromResult(false);

        payment.Id = Guid.NewGuid();
        payment.Subscription = _subscriptions.Single(s => s.Id == payment.SubscriptionId);
        _payments.Add(payment);
        SaveCount++;
        return Task.FromResult(true);
    }

    public Task<(IReadOnlyList<SubscriptionPayment> Items, int Total)> ListPaymentsAsync(
        Guid accountId, int page, int pageSize, CancellationToken ct = default)
    {
        var mine = _payments.Where(p => p.Subscription.AccountId == accountId).ToList();
        var items = mine
            .OrderBy(p => p.PaidAt is null)
            .ThenByDescending(p => p.PaidAt)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult<(IReadOnlyList<SubscriptionPayment>, int)>((items, mine.Count));
    }

    private static bool IsCurrent(SubscriptionStatus status) =>
        status is SubscriptionStatus.ACTIVE or SubscriptionStatus.PAST_DUE;
}
