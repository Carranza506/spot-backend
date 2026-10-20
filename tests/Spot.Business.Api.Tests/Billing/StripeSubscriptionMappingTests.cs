using Spot.Business.Api.Billing;
using Spot.Business.Api.Models;
using Spot.Business.Api.Options;

namespace Spot.Business.Api.Tests.Billing;

public sealed class StripeSubscriptionMappingTests
{
    // ---------- MapStatus ----------

    [Theory]
    [InlineData("active", SubscriptionStatus.ACTIVE)]
    [InlineData("trialing", SubscriptionStatus.ACTIVE)]
    [InlineData("past_due", SubscriptionStatus.PAST_DUE)]
    [InlineData("unpaid", SubscriptionStatus.PAST_DUE)]
    [InlineData("canceled", SubscriptionStatus.CANCELED)]
    [InlineData("incomplete_expired", SubscriptionStatus.EXPIRED)]
    [InlineData("paused", SubscriptionStatus.EXPIRED)]
    public void MapStatus_MapsEveryStripeStatusSpotStores(string stripeStatus, SubscriptionStatus expected) =>
        Assert.Equal(expected, StripeSubscriptionMapping.MapStatus(stripeStatus));

    [Theory]
    [InlineData("incomplete")]
    [InlineData("some_future_status")]
    [InlineData("ACTIVE")]
    public void MapStatus_IncompleteOrUnknown_IsNull(string stripeStatus) =>
        Assert.Null(StripeSubscriptionMapping.MapStatus(stripeStatus));

    // ---------- CrcFromMinorUnits ----------

    [Theory]
    [InlineData(1000000L, "10000.00")] // the PRO price as Stripe stores it
    [InlineData(0L, "0")]
    [InlineData(1050L, "10.50")]
    public void CrcFromMinorUnits_DividesByOneHundred_CrcIsTwoDecimalInStripe(long minorUnits, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            StripeSubscriptionMapping.CrcFromMinorUnits(minorUnits));

    // ---------- Apply ----------

    [Fact]
    public void Apply_SameValues_ReportsNoChange()
    {
        var periodEnd = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        var subscription = new Subscription
        {
            Status = SubscriptionStatus.ACTIVE,
            CurrentPeriodEnd = periodEnd,
            StripeCustomerId = "cus_1",
            PlanId = Guid.NewGuid(),
        };
        var snapshot = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", periodEnd, false, "price_pro", null);

        Assert.False(StripeSubscriptionMapping.Apply(subscription, snapshot, SubscriptionStatus.ACTIVE, plan: null));
    }

    [Fact]
    public void Apply_NewPeriodAndCancelFlag_UpdatesAndReportsChange()
    {
        var subscription = new Subscription
        {
            Status = SubscriptionStatus.ACTIVE,
            CurrentPeriodEnd = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
            StripeCustomerId = "cus_1",
        };
        var newEnd = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        var snapshot = new StripeSubscriptionSnapshot("sub_1", "cus_1", "past_due", newEnd, true, "price_pro", null);

        Assert.True(StripeSubscriptionMapping.Apply(subscription, snapshot, SubscriptionStatus.PAST_DUE, plan: null));
        Assert.Equal(SubscriptionStatus.PAST_DUE, subscription.Status);
        Assert.Equal(newEnd, subscription.CurrentPeriodEnd);
        Assert.True(subscription.CancelAtPeriodEnd);
    }

    // ---------- StripeOptions.PriceIdFor ----------

    [Fact]
    public void PriceIdFor_Pro_FallsBackToConfiguredPrice()
    {
        var options = new StripeOptions { ProPriceId = "price_from_config" };

        Assert.Equal("price_from_config", options.PriceIdFor(new Plan { Code = PlanCode.PRO }));
    }

    [Fact]
    public void PriceIdFor_PlanColumnSet_WinsOverConfiguration()
    {
        var options = new StripeOptions { ProPriceId = "price_from_config" };

        Assert.Equal("price_from_db", options.PriceIdFor(new Plan { Code = PlanCode.PRO, StripePriceId = "price_from_db" }));
    }

    [Fact]
    public void PriceIdFor_Free_IsNull()
    {
        var options = new StripeOptions { ProPriceId = "price_from_config" };

        Assert.Null(options.PriceIdFor(new Plan { Code = PlanCode.FREE }));
    }

    // ---------- StripeOptionsValidator ----------

    [Fact]
    public void Validator_TestModeSettings_Succeed()
    {
        var result = new StripeOptionsValidator().Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_LiveKey_IsRejected_PaymentsAreTestModeOnly()
    {
        var options = ValidOptions();
        options.SecretKey = "sk_live_abc";

        var result = new StripeOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("SecretKey"));
    }

    [Fact]
    public void Validator_MissingValues_ReportsEachOne()
    {
        var result = new StripeOptionsValidator().Validate(null, new StripeOptions());

        Assert.True(result.Failed);
        Assert.Equal(5, result.Failures!.Count());
    }

    private static StripeOptions ValidOptions() => new()
    {
        SecretKey = "sk_test_abc",
        ProPriceId = "price_abc",
        WebhookSecretFile = "/run/stripe/webhook-secret",
        SuccessUrl = "http://localhost:5173/billing/success?session_id={CHECKOUT_SESSION_ID}",
        CancelUrl = "http://localhost:5173/billing/cancel",
    };
}
