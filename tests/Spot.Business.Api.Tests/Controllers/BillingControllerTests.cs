using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Business.Api.Billing;
using Spot.Business.Api.Models;
using Spot.Business.Api.Tests.Fakes;

namespace Spot.Business.Api.Tests.Controllers;

[Collection(ApiFactoryCollection.Name)]
public class BillingControllerTests : IClassFixture<BusinessesApiFactory>
{
    private readonly BusinessesApiFactory _factory;

    public BillingControllerTests(BusinessesApiFactory factory)
    {
        _factory = factory;
        _factory.BillingRepository.Reset();
        _factory.StripeGateway.Reset();
    }

    // ---------- GET /business/billing/plans ----------

    [Fact]
    public async Task ListPlans_Anonymous_Returns200WithPlansAndFeatures()
    {
        var response = await _factory.CreateClient().GetAsync("/business/billing/plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plans = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(["FREE", "PRO"], plans.Select(p => p.GetProperty("code").GetString()));
        Assert.Equal(5, plans[0].GetProperty("features").GetProperty("maxServices").GetInt32());
        // Unlimited is an explicit null, not a missing property (contract: required, nullable).
        Assert.Equal(JsonValueKind.Null, plans[1].GetProperty("features").GetProperty("maxServices").ValueKind);
        Assert.Equal(10000m, plans[1].GetProperty("priceCrc").GetDecimal());
        Assert.False(plans[1].TryGetProperty("stripePriceId", out _));
    }

    // ---------- GET /business/billing/subscription ----------

    [Fact]
    public async Task GetMySubscription_NoToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/business/billing/subscription");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMySubscription_ClientRole_Returns403()
    {
        var response = await ClientFor(Guid.NewGuid(), "CLIENT").GetAsync("/business/billing/subscription");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMySubscription_NoSubscription_ReturnsFreeWithNulls()
    {
        var response = await ClientFor(Guid.NewGuid()).GetAsync("/business/billing/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FREE", body.GetProperty("plan").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("currentPeriodEnd").ValueKind);
        Assert.False(body.GetProperty("cancelAtPeriodEnd").GetBoolean());
    }

    [Fact]
    public async Task GetMySubscription_ActiveSubscription_ReturnsProAndStatus()
    {
        var accountId = Guid.NewGuid();
        SeedActive(accountId);

        var body = await ClientFor(accountId).GetFromJsonAsync<JsonElement>("/business/billing/subscription");

        Assert.Equal("PRO", body.GetProperty("plan").GetProperty("code").GetString());
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
    }

    // ---------- POST /business/billing/checkout ----------

    [Fact]
    public async Task Checkout_Pro_Returns200WithTheStripeUrl()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync("/business/billing/checkout", new { planCode = "PRO" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("https://checkout.stripe.com/", body.GetProperty("checkoutUrl").GetString());
        Assert.Equal(BusinessesApiFactory.ProPriceId, Assert.Single(_factory.StripeGateway.CheckoutRequests).PriceId);
        Assert.Empty(_factory.BillingRepository.Subscriptions);
    }

    [Theory]
    [InlineData("FREE")]
    [InlineData("GOLD")]
    public async Task Checkout_FreeOrUnknownPlan_Returns400OnPlanCode(string planCode)
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync("/business/billing/checkout", new { planCode });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Equal("planCode", body.GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Checkout_MissingPlanCode_Returns400()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync("/business/billing/checkout", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_AlreadySubscribed_Returns409()
    {
        var accountId = Guid.NewGuid();
        SeedActive(accountId);

        var response = await ClientFor(accountId).PostAsJsonAsync("/business/billing/checkout", new { planCode = "PRO" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ALREADY_SUBSCRIBED", body.GetProperty("code").GetString());
    }

    // ---------- POST /business/billing/subscription/cancel ----------

    [Fact]
    public async Task Cancel_NoSubscription_Returns404()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/business/billing/subscription/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_ActiveSubscription_Returns200WithCancelAtPeriodEnd()
    {
        var accountId = Guid.NewGuid();
        var subscription = SeedActive(accountId);
        _factory.StripeGateway.Subscriptions[subscription.StripeSubscriptionId] = new(
            subscription.StripeSubscriptionId, "cus_1", "active", subscription.CurrentPeriodEnd, false,
            BusinessesApiFactory.ProPriceId, accountId);

        var response = await ClientFor(accountId).PostAsync("/business/billing/subscription/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("cancelAtPeriodEnd").GetBoolean());
    }

    // ---------- GET /business/billing/payments ----------

    [Fact]
    public async Task ListPayments_InvalidPaging_Returns400()
    {
        var response = await ClientFor(Guid.NewGuid()).GetAsync("/business/billing/payments?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListPayments_ReturnsTheCallersPaymentsPaginated()
    {
        var accountId = Guid.NewGuid();
        var subscription = SeedActive(accountId);
        _factory.BillingRepository.SeedPayment(new SubscriptionPayment
        {
            SubscriptionId = subscription.Id, StripeInvoiceId = "in_1", Status = SubscriptionPaymentStatus.PAID,
            AmountCrc = 10000m, PaidAt = DateTimeOffset.UtcNow,
        });

        var body = await ClientFor(accountId).GetFromJsonAsync<JsonElement>("/business/billing/payments");

        var payment = Assert.Single(body.GetProperty("data").EnumerateArray());
        Assert.Equal(10000m, payment.GetProperty("amountCrc").GetDecimal());
        Assert.Equal("PAID", payment.GetProperty("status").GetString());
        Assert.False(payment.TryGetProperty("stripeInvoiceId", out _));
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    // ---------- POST /business/billing/webhooks/stripe ----------

    [Fact]
    public async Task Webhook_MissingSignature_Returns400()
    {
        var response = await _factory.CreateClient().PostAsync("/business/billing/webhooks/stripe", Json("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Returns400AndStoresNothing()
    {
        _factory.StripeGateway.NextEvent = new StripeWebhookEvent("evt_1", StripeEventTypes.CustomerSubscriptionUpdated, "sub_1");

        var response = await SendWebhook("t=1,v1=forged");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.BillingRepository.Subscriptions);
    }

    [Fact]
    public async Task Webhook_ValidCheckoutCompleted_Anonymous_Returns200AndActivatesTheSubscription()
    {
        var accountId = Guid.NewGuid();
        var gateway = _factory.StripeGateway;
        gateway.Sessions["cs_1"] = new StripeCheckoutSessionSnapshot("cs_1", "subscription", "sub_1", accountId.ToString());
        gateway.Subscriptions["sub_1"] = new StripeSubscriptionSnapshot(
            "sub_1", "cus_1", "active", DateTimeOffset.UtcNow.AddMonths(1), false, BusinessesApiFactory.ProPriceId, accountId);
        gateway.NextEvent = new StripeWebhookEvent("evt_1", StripeEventTypes.CheckoutSessionCompleted, "cs_1");

        var response = await SendWebhook(FakeStripeBillingGateway.ValidSignature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var subscription = Assert.Single(_factory.BillingRepository.Subscriptions);
        Assert.Equal(accountId, subscription.AccountId);
        Assert.Equal(SubscriptionStatus.ACTIVE, subscription.Status);
    }

    [Fact]
    public async Task Webhook_UnhandledEventType_Returns200()
    {
        _factory.StripeGateway.NextEvent = new StripeWebhookEvent("evt_1", "customer.created", "cus_1");

        var response = await SendWebhook(FakeStripeBillingGateway.ValidSignature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- Helpers ----------

    private HttpClient ClientFor(Guid accountId, string role = "BUSINESS")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.IssueAccessToken(accountId, role));
        return client;
    }

    private Subscription SeedActive(Guid accountId) => _factory.BillingRepository.Seed(new Subscription
    {
        AccountId = accountId,
        PlanId = _factory.BillingRepository.ProPlan.Id,
        Status = SubscriptionStatus.ACTIVE,
        StripeCustomerId = "cus_1",
        StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
        CurrentPeriodEnd = DateTimeOffset.UtcNow.AddMonths(1),
    });

    private Task<HttpResponseMessage> SendWebhook(string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/business/billing/webhooks/stripe") { Content = Json("{}") };
        request.Headers.Add("Stripe-Signature", signature);
        return _factory.CreateClient().SendAsync(request);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
