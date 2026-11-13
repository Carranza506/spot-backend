using Microsoft.Extensions.Logging;
using Spot.Business.Api.Billing;
using Spot.Business.Api.Models;
using Spot.Business.Api.Options;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;

namespace Spot.Business.Api.Tests.Services;

public sealed class StripeWebhookServiceTests
{
    private const string ProPriceId = "price_pro_test";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = Now.AddMonths(1);

    private readonly FakeStripeBillingGateway _stripe = new();
    private readonly FakeBillingRepository _repository = new();
    private readonly CapturingLogger<StripeWebhookService> _logger = new();
    private readonly StripeWebhookService _service;
    private readonly Guid _accountId = Guid.NewGuid();

    public StripeWebhookServiceTests() =>
        _service = new StripeWebhookService(
            _stripe,
            new FakeStripeWebhookSecretProvider(),
            _repository,
            Microsoft.Extensions.Options.Options.Create(new StripeOptions { ProPriceId = ProPriceId }),
            new FixedTimeProvider(Now),
            _logger);

    // ---------- Signature ----------

    [Fact]
    public async Task HandleAsync_MissingSignature_ThrowsInvalidWebhook()
    {
        await Assert.ThrowsAsync<InvalidStripeWebhookException>(() => _service.HandleAsync("{}", null));
        await Assert.ThrowsAsync<InvalidStripeWebhookException>(() => _service.HandleAsync("{}", "  "));
    }

    [Fact]
    public async Task HandleAsync_InvalidSignature_ThrowsAndStoresNothing()
    {
        StripeHasSubscription("sub_1", "active");
        _stripe.NextEvent = new StripeWebhookEvent("evt_1", StripeEventTypes.CustomerSubscriptionUpdated, "sub_1");

        await Assert.ThrowsAsync<InvalidStripeWebhookException>(() => _service.HandleAsync("{}", "t=1,v1=forged"));
        Assert.Empty(_repository.Subscriptions);
    }

    [Fact]
    public async Task HandleAsync_ValidSignature_ProcessesTheEvent()
    {
        StripeHasSubscription("sub_1", "active");
        _stripe.NextEvent = new StripeWebhookEvent("evt_1", StripeEventTypes.CustomerSubscriptionUpdated, "sub_1");

        await _service.HandleAsync("{}", FakeStripeBillingGateway.ValidSignature);

        Assert.Single(_repository.Subscriptions);
    }

    // ---------- checkout.session.completed ----------

    [Fact]
    public async Task CheckoutCompleted_CreatesAnActiveProSubscriptionForTheAccount()
    {
        StripeHasCheckout("cs_1", "sub_1");
        StripeHasSubscription("sub_1", "active");

        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));

        var subscription = Assert.Single(_repository.Subscriptions);
        Assert.Equal(_accountId, subscription.AccountId);
        Assert.Equal(_repository.ProPlan.Id, subscription.PlanId);
        Assert.Equal(SubscriptionStatus.ACTIVE, subscription.Status);
        Assert.Equal("cus_1", subscription.StripeCustomerId);
        Assert.Equal("sub_1", subscription.StripeSubscriptionId);
        Assert.Equal(PeriodEnd, subscription.CurrentPeriodEnd);
        Assert.False(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task CheckoutCompleted_Twice_HasNoExtraEffect()
    {
        StripeHasCheckout("cs_1", "sub_1");
        StripeHasSubscription("sub_1", "active");

        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));
        var savesAfterFirst = _repository.SaveCount;
        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));

        Assert.Single(_repository.Subscriptions);
        Assert.Equal(savesAfterFirst, _repository.SaveCount);
    }

    [Fact]
    public async Task CheckoutCompleted_NoMetadata_FallsBackToClientReferenceId()
    {
        StripeHasCheckout("cs_1", "sub_1");
        StripeHasSubscription("sub_1", "active", accountId: null);

        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));

        Assert.Equal(_accountId, Assert.Single(_repository.Subscriptions).AccountId);
    }

    [Fact]
    public async Task CheckoutCompleted_PaymentModeSession_IsIgnored()
    {
        _stripe.Sessions["cs_1"] = new StripeCheckoutSessionSnapshot("cs_1", "payment", null, _accountId.ToString());

        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));

        Assert.Empty(_repository.Subscriptions);
    }

    [Fact]
    public async Task CheckoutCompleted_IncompleteSubscription_StoresNothingYet()
    {
        StripeHasCheckout("cs_1", "sub_1");
        StripeHasSubscription("sub_1", "incomplete");

        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));

        Assert.Empty(_repository.Subscriptions);
    }

    [Fact]
    public async Task CheckoutCompleted_UnknownPrice_IsIgnored()
    {
        StripeHasCheckout("cs_1", "sub_1");
        _stripe.Subscriptions["sub_1"] = Snapshot("sub_1", "active") with { PriceId = "price_someone_else" };

        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));

        Assert.Empty(_repository.Subscriptions);
    }

    [Fact]
    public async Task CheckoutCompleted_AccountAlreadySubscribed_DoesNotStoreTheSecondOne_AndLogsAnError()
    {
        _repository.Seed(new Subscription
        {
            AccountId = _accountId, PlanId = _repository.ProPlan.Id, Status = SubscriptionStatus.ACTIVE,
            StripeCustomerId = "cus_1", StripeSubscriptionId = "sub_first", CurrentPeriodEnd = PeriodEnd,
        });
        StripeHasCheckout("cs_2", "sub_second");
        StripeHasSubscription("sub_second", "active");

        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_2"));

        Assert.Equal("sub_first", Assert.Single(_repository.Subscriptions).StripeSubscriptionId);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("sub_second"));
    }

    // ---------- customer.subscription.updated / deleted ----------

    [Theory]
    [InlineData("past_due", SubscriptionStatus.PAST_DUE)]
    [InlineData("unpaid", SubscriptionStatus.PAST_DUE)]
    [InlineData("canceled", SubscriptionStatus.CANCELED)]
    [InlineData("incomplete_expired", SubscriptionStatus.EXPIRED)]
    public async Task SubscriptionUpdated_MapsStripeStatusOntoTheExistingRow(string stripeStatus, SubscriptionStatus expected)
    {
        var existing = SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", stripeStatus);

        await _service.ProcessAsync(Event(StripeEventTypes.CustomerSubscriptionUpdated, "sub_1"));

        Assert.Equal(expected, existing.Status);
    }

    [Fact]
    public async Task SubscriptionUpdated_CancelAtPeriodEndAndNewPeriod_AreStored()
    {
        var existing = SeedActiveSubscription("sub_1");
        var newEnd = PeriodEnd.AddMonths(1);
        _stripe.Subscriptions["sub_1"] = Snapshot("sub_1", "active") with { CurrentPeriodEnd = newEnd, CancelAtPeriodEnd = true };

        await _service.ProcessAsync(Event(StripeEventTypes.CustomerSubscriptionUpdated, "sub_1"));

        Assert.Equal(newEnd, existing.CurrentPeriodEnd);
        Assert.True(existing.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task SubscriptionDeleted_MarksTheRowCanceled()
    {
        var existing = SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "canceled");

        await _service.ProcessAsync(Event(StripeEventTypes.CustomerSubscriptionDeleted, "sub_1"));

        Assert.Equal(SubscriptionStatus.CANCELED, existing.Status);
    }

    [Fact]
    public async Task SubscriptionUpdated_Twice_SavesOnlyOnce()
    {
        SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "past_due");

        await _service.ProcessAsync(Event(StripeEventTypes.CustomerSubscriptionUpdated, "sub_1"));
        await _service.ProcessAsync(Event(StripeEventTypes.CustomerSubscriptionUpdated, "sub_1"));

        Assert.Equal(1, _repository.SaveCount);
    }

    [Fact]
    public async Task SubscriptionUpdated_ToIncomplete_LeavesTheStoredRowAlone()
    {
        var existing = SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "incomplete");

        await _service.ProcessAsync(Event(StripeEventTypes.CustomerSubscriptionUpdated, "sub_1"));

        Assert.Equal(SubscriptionStatus.ACTIVE, existing.Status);
        Assert.Equal(0, _repository.SaveCount);
    }

    [Fact]
    public async Task SubscriptionWithoutSpotAccount_LikeStripeTrigger_IsIgnored()
    {
        StripeHasSubscription("sub_fixture", "active", accountId: null);

        await _service.ProcessAsync(Event(StripeEventTypes.CustomerSubscriptionUpdated, "sub_fixture"));

        Assert.Empty(_repository.Subscriptions);
    }

    // ---------- invoice.paid / invoice.payment_failed ----------

    [Fact]
    public async Task InvoicePaid_RecordsAPaidPaymentInColones()
    {
        var subscription = SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "active");
        var paidAt = Now.AddMinutes(-5);
        StripeHasInvoice("in_1", "sub_1", amountPaid: 1000000, amountDue: 1000000, paidAt: paidAt);

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        var payment = Assert.Single(_repository.Payments);
        Assert.Equal(subscription.Id, payment.SubscriptionId);
        Assert.Equal(10000.00m, payment.AmountCrc);
        Assert.Equal(SubscriptionPaymentStatus.PAID, payment.Status);
        Assert.Equal("in_1", payment.StripeInvoiceId);
        Assert.Equal(paidAt, payment.PaidAt);
    }

    [Fact]
    public async Task InvoicePaid_Twice_RecordsOnePayment()
    {
        SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "active");
        StripeHasInvoice("in_1", "sub_1", amountPaid: 1000000, amountDue: 1000000, paidAt: Now);

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));
        var savesAfterFirst = _repository.SaveCount;
        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        Assert.Single(_repository.Payments);
        Assert.Equal(savesAfterFirst, _repository.SaveCount);
    }

    [Fact]
    public async Task InvoicePaid_BeforeCheckoutCompleted_CreatesTheSubscriptionFromItsMetadata()
    {
        StripeHasSubscription("sub_1", "active");
        StripeHasInvoice("in_1", "sub_1", amountPaid: 1000000, amountDue: 1000000, paidAt: Now);

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        var subscription = Assert.Single(_repository.Subscriptions);
        Assert.Equal(_accountId, subscription.AccountId);
        Assert.Equal(subscription.Id, Assert.Single(_repository.Payments).SubscriptionId);

        // checkout.session.completed arriving afterwards changes nothing.
        StripeHasCheckout("cs_1", "sub_1");
        await _service.ProcessAsync(Event(StripeEventTypes.CheckoutSessionCompleted, "cs_1"));
        Assert.Single(_repository.Subscriptions);
    }

    [Fact]
    public async Task InvoicePaymentFailed_RecordsAFailedPaymentWithoutPaidAt_AndSubscriptionGoesPastDue()
    {
        var subscription = SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "past_due");
        StripeHasInvoice("in_1", "sub_1", amountPaid: 0, amountDue: 1000000, paidAt: null);

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaymentFailed, "in_1"));

        var payment = Assert.Single(_repository.Payments);
        Assert.Equal(SubscriptionPaymentStatus.FAILED, payment.Status);
        Assert.Equal(10000.00m, payment.AmountCrc);
        Assert.Null(payment.PaidAt);
        Assert.Equal(SubscriptionStatus.PAST_DUE, subscription.Status);
    }

    [Fact]
    public async Task InvoiceFailedThenPaidOnRetry_IsOnePaymentThatEndsPaid()
    {
        SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "active");
        StripeHasInvoice("in_1", "sub_1", amountPaid: 0, amountDue: 1000000, paidAt: null);
        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaymentFailed, "in_1"));

        StripeHasInvoice("in_1", "sub_1", amountPaid: 1000000, amountDue: 1000000, paidAt: Now);
        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        var payment = Assert.Single(_repository.Payments);
        Assert.Equal(SubscriptionPaymentStatus.PAID, payment.Status);
        Assert.Equal(Now, payment.PaidAt);
    }

    [Fact]
    public async Task InvoicePaidThenALateFailedEvent_StaysPaid()
    {
        SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "active");
        StripeHasInvoice("in_1", "sub_1", amountPaid: 1000000, amountDue: 1000000, paidAt: Now);
        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaymentFailed, "in_1"));

        Assert.Equal(SubscriptionPaymentStatus.PAID, Assert.Single(_repository.Payments).Status);
    }

    [Fact]
    public async Task InvoicePaid_WithoutPaidAtFromStripe_UsesTheTimeItWasProcessed()
    {
        SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "active");
        StripeHasInvoice("in_1", "sub_1", amountPaid: 1000000, amountDue: 1000000, paidAt: null);

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        Assert.Equal(Now, Assert.Single(_repository.Payments).PaidAt);
    }

    [Fact]
    public async Task InvoiceWithoutSubscription_IsIgnored()
    {
        StripeHasInvoice("in_1", subscriptionId: null, amountPaid: 500, amountDue: 500, paidAt: Now);

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        Assert.Empty(_repository.Payments);
    }

    [Fact]
    public async Task InvoiceInAnotherCurrency_IsNotRecorded()
    {
        SeedActiveSubscription("sub_1");
        StripeHasSubscription("sub_1", "active");
        _stripe.Invoices["in_1"] = new StripeInvoiceSnapshot("in_1", "sub_1", "usd", 2000, 2000, Now);

        await _service.ProcessAsync(Event(StripeEventTypes.InvoicePaid, "in_1"));

        Assert.Empty(_repository.Payments);
    }

    // ---------- Other events ----------

    [Fact]
    public async Task UnhandledEventType_IsIgnored()
    {
        await _service.ProcessAsync(Event("customer.created", "cus_1"));

        Assert.Empty(_repository.Subscriptions);
        Assert.Equal(0, _repository.SaveCount);
    }

    // ---------- Helpers ----------

    private StripeWebhookEvent Event(string type, string objectId) => new($"evt_{Guid.NewGuid():N}", type, objectId);

    private StripeSubscriptionSnapshot Snapshot(string id, string status, Guid? accountId = default) =>
        new(id, "cus_1", status, PeriodEnd, false, ProPriceId, accountId);

    private void StripeHasSubscription(string id, string status) =>
        _stripe.Subscriptions[id] = Snapshot(id, status, _accountId);

    private void StripeHasSubscription(string id, string status, Guid? accountId) =>
        _stripe.Subscriptions[id] = Snapshot(id, status, accountId);

    private void StripeHasCheckout(string sessionId, string subscriptionId) =>
        _stripe.Sessions[sessionId] = new StripeCheckoutSessionSnapshot(sessionId, "subscription", subscriptionId, _accountId.ToString());

    private void StripeHasInvoice(string id, string? subscriptionId, long amountPaid, long amountDue, DateTimeOffset? paidAt) =>
        _stripe.Invoices[id] = new StripeInvoiceSnapshot(id, subscriptionId, "crc", amountPaid, amountDue, paidAt);

    private Subscription SeedActiveSubscription(string stripeSubscriptionId) => _repository.Seed(new Subscription
    {
        AccountId = _accountId,
        PlanId = _repository.ProPlan.Id,
        Status = SubscriptionStatus.ACTIVE,
        StripeCustomerId = "cus_1",
        StripeSubscriptionId = stripeSubscriptionId,
        CurrentPeriodEnd = PeriodEnd,
    });
}
