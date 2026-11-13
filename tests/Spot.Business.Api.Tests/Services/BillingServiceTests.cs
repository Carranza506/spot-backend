using Spot.Business.Api.Models;
using Spot.Business.Api.Options;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;

namespace Spot.Business.Api.Tests.Services;

public sealed class BillingServiceTests
{
    private const string ProPriceId = "price_pro_test";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);

    private readonly FakeStripeBillingGateway _stripe = new();
    private readonly FakeBillingRepository _repository = new();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly BillingService _service;
    private readonly Guid _accountId = Guid.NewGuid();

    public BillingServiceTests() =>
        _service = new BillingService(
            _repository,
            _stripe,
            Microsoft.Extensions.Options.Options.Create(new StripeOptions { ProPriceId = ProPriceId }),
            _clock,
            new CapturingLogger<BillingService>());

    // ---------- ListPlansAsync ----------

    [Fact]
    public async Task ListPlansAsync_ReturnsActivePlansCheapestFirst_WithTheirFeatures()
    {
        var plans = await _service.ListPlansAsync();

        Assert.Equal(["FREE", "PRO"], plans.Select(p => p.Code));
        Assert.Equal(new(5, 3, 3), plans[0].Features);
        Assert.Equal(new(null, null, null), plans[1].Features);
        Assert.Equal(10000m, plans[1].PriceCrc);
        Assert.Equal("MONTHLY", plans[1].Interval);
    }

    // ---------- GetMySubscriptionAsync (FREE fallback) ----------

    [Fact]
    public async Task GetMySubscriptionAsync_NoSubscription_IsFree()
    {
        var result = await _service.GetMySubscriptionAsync(_accountId);

        Assert.Equal("FREE", result.Plan.Code);
        Assert.Null(result.Id);
        Assert.Null(result.Status);
        Assert.Null(result.CurrentPeriodEnd);
        Assert.False(result.CancelAtPeriodEnd);
    }

    [Theory]
    [InlineData(SubscriptionStatus.ACTIVE)]
    [InlineData(SubscriptionStatus.PAST_DUE)]
    public async Task GetMySubscriptionAsync_CurrentSubscriptionWithinItsPeriod_IsPro(SubscriptionStatus status)
    {
        var subscription = Seed(status, periodEnd: Now.AddDays(10));

        var result = await _service.GetMySubscriptionAsync(_accountId);

        Assert.Equal("PRO", result.Plan.Code);
        Assert.Equal(subscription.Id, result.Id);
        Assert.Equal(status.ToString(), result.Status);
        Assert.Equal(Now.AddDays(10), result.CurrentPeriodEnd);
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ActiveRowButPeriodOver_IsFree()
    {
        Seed(SubscriptionStatus.ACTIVE, periodEnd: Now.AddSeconds(-1));

        var result = await _service.GetMySubscriptionAsync(_accountId);

        Assert.Equal("FREE", result.Plan.Code);
        Assert.Null(result.Status);
    }

    [Theory]
    [InlineData(SubscriptionStatus.CANCELED)]
    [InlineData(SubscriptionStatus.EXPIRED)]
    public async Task GetMySubscriptionAsync_CanceledOrExpired_IsFree_EvenWithinThePeriod(SubscriptionStatus status)
    {
        Seed(status, periodEnd: Now.AddDays(10));

        var result = await _service.GetMySubscriptionAsync(_accountId);

        Assert.Equal("FREE", result.Plan.Code);
    }

    [Fact]
    public async Task GetMySubscriptionAsync_OtherAccountsSubscription_DoesNotCount()
    {
        _repository.Seed(new Subscription
        {
            AccountId = Guid.NewGuid(), PlanId = _repository.ProPlan.Id, Status = SubscriptionStatus.ACTIVE,
            StripeCustomerId = "cus_x", StripeSubscriptionId = "sub_x", CurrentPeriodEnd = Now.AddDays(10),
        });

        Assert.Equal("FREE", (await _service.GetMySubscriptionAsync(_accountId)).Plan.Code);
    }

    // ---------- CreateCheckoutSessionAsync ----------

    [Fact]
    public async Task CreateCheckoutSessionAsync_Pro_AsksStripeForTheConfiguredPrice_AndActivatesNothing()
    {
        var result = await _service.CreateCheckoutSessionAsync(_accountId, "PRO");

        Assert.Equal("https://checkout.stripe.com/c/pay/cs_test_fake", result.CheckoutUrl);
        var request = Assert.Single(_stripe.CheckoutRequests);
        Assert.Equal(_accountId, request.AccountId);
        Assert.Equal(PlanCode.PRO, request.PlanCode);
        Assert.Equal(ProPriceId, request.PriceId);
        Assert.Null(request.CustomerId);
        Assert.Empty(_repository.Subscriptions);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_ReusesTheAccountsPreviousStripeCustomer()
    {
        Seed(SubscriptionStatus.CANCELED, periodEnd: Now.AddDays(-5), customerId: "cus_previous");

        await _service.CreateCheckoutSessionAsync(_accountId, "PRO");

        Assert.Equal("cus_previous", Assert.Single(_stripe.CheckoutRequests).CustomerId);
    }

    [Theory]
    [InlineData("FREE")]
    [InlineData("pro")]
    [InlineData("1")]
    [InlineData("ENTERPRISE")]
    [InlineData("")]
    public async Task CreateCheckoutSessionAsync_FreeOrUnknownPlan_IsInvalid(string planCode)
    {
        await Assert.ThrowsAsync<InvalidPlanException>(() => _service.CreateCheckoutSessionAsync(_accountId, planCode));
        Assert.Empty(_stripe.CheckoutRequests);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_InactivePlan_IsInvalid()
    {
        _repository.ProPlan.IsActive = false;

        await Assert.ThrowsAsync<InvalidPlanException>(() => _service.CreateCheckoutSessionAsync(_accountId, "PRO"));
    }

    [Theory]
    [InlineData(SubscriptionStatus.ACTIVE)]
    [InlineData(SubscriptionStatus.PAST_DUE)]
    public async Task CreateCheckoutSessionAsync_AlreadySubscribed_Throws(SubscriptionStatus status)
    {
        Seed(status, periodEnd: Now.AddDays(10));

        await Assert.ThrowsAsync<AlreadySubscribedException>(() => _service.CreateCheckoutSessionAsync(_accountId, "PRO"));
        Assert.Empty(_stripe.CheckoutRequests);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_PreviousSubscriptionCanceled_CanSubscribeAgain()
    {
        Seed(SubscriptionStatus.CANCELED, periodEnd: Now.AddDays(-5));

        await _service.CreateCheckoutSessionAsync(_accountId, "PRO");

        Assert.Single(_stripe.CheckoutRequests);
    }

    // ---------- CancelMySubscriptionAsync ----------

    [Fact]
    public async Task CancelMySubscriptionAsync_AsksStripeToCancelAtPeriodEnd_AndStoresTheFlag()
    {
        var subscription = Seed(SubscriptionStatus.ACTIVE, periodEnd: Now.AddDays(10));
        StripeMirrors(subscription);

        var result = await _service.CancelMySubscriptionAsync(_accountId);

        Assert.Equal(["sub_1"], _stripe.CancelRequests);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.True(result!.CancelAtPeriodEnd);
        Assert.Equal("ACTIVE", result.Status); // features stay until the period ends
    }

    [Fact]
    public async Task CancelMySubscriptionAsync_AlreadyCanceling_IsIdempotent()
    {
        var subscription = Seed(SubscriptionStatus.ACTIVE, periodEnd: Now.AddDays(10));
        subscription.CancelAtPeriodEnd = true;

        var result = await _service.CancelMySubscriptionAsync(_accountId);

        Assert.Empty(_stripe.CancelRequests);
        Assert.True(result!.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task CancelMySubscriptionAsync_NothingToCancel_IsNull()
    {
        Seed(SubscriptionStatus.CANCELED, periodEnd: Now.AddDays(-1));

        Assert.Null(await _service.CancelMySubscriptionAsync(_accountId));
        Assert.Empty(_stripe.CancelRequests);
    }

    // ---------- ListMyPaymentsAsync ----------

    [Fact]
    public async Task ListMyPaymentsAsync_PaidNewestFirst_ThenFailed_OnlyTheCallersOwn()
    {
        var mine = Seed(SubscriptionStatus.ACTIVE, periodEnd: Now.AddDays(10));
        var someoneElses = _repository.Seed(new Subscription
        {
            AccountId = Guid.NewGuid(), PlanId = _repository.ProPlan.Id, Status = SubscriptionStatus.ACTIVE,
            StripeCustomerId = "cus_x", StripeSubscriptionId = "sub_x", CurrentPeriodEnd = Now.AddDays(10),
        });
        _repository.SeedPayment(Payment(mine, "in_old", SubscriptionPaymentStatus.PAID, Now.AddMonths(-1)));
        _repository.SeedPayment(Payment(mine, "in_failed", SubscriptionPaymentStatus.FAILED, null));
        _repository.SeedPayment(Payment(mine, "in_new", SubscriptionPaymentStatus.PAID, Now));
        _repository.SeedPayment(Payment(someoneElses, "in_other", SubscriptionPaymentStatus.PAID, Now));

        var page = await _service.ListMyPaymentsAsync(_accountId, page: 1, pageSize: 20);

        Assert.Equal(3, page.Pagination.Total);
        Assert.Equal(["PAID", "PAID", "FAILED"], page.Data.Select(p => p.Status));
        Assert.Equal(Now, page.Data[0].PaidAt);
    }

    // ---------- Helpers ----------

    private Subscription Seed(SubscriptionStatus status, DateTimeOffset periodEnd, string customerId = "cus_1") =>
        _repository.Seed(new Subscription
        {
            AccountId = _accountId,
            PlanId = _repository.ProPlan.Id,
            Status = status,
            StripeCustomerId = customerId,
            StripeSubscriptionId = "sub_1",
            CurrentPeriodEnd = periodEnd,
        });

    private void StripeMirrors(Subscription subscription) =>
        _stripe.Subscriptions[subscription.StripeSubscriptionId] = new(
            subscription.StripeSubscriptionId, subscription.StripeCustomerId, "active",
            subscription.CurrentPeriodEnd, false, ProPriceId, _accountId);

    private static SubscriptionPayment Payment(Subscription subscription, string invoiceId, SubscriptionPaymentStatus status, DateTimeOffset? paidAt) =>
        new()
        {
            SubscriptionId = subscription.Id,
            StripeInvoiceId = invoiceId,
            Status = status,
            AmountCrc = 10000m,
            PaidAt = paidAt,
        };
}
