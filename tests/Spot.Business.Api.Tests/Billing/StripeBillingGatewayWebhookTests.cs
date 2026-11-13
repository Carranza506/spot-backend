using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Spot.Business.Api.Billing;
using Spot.Business.Api.Options;

namespace Spot.Business.Api.Tests.Billing;

/// <summary>
/// The real <see cref="StripeBillingGateway.VerifyWebhook"/> against payloads signed the way Stripe
/// signs them (HMAC-SHA256 of "{timestamp}.{body}" with the endpoint secret). No network: signature
/// verification never calls the Stripe API.
/// </summary>
public sealed class StripeBillingGatewayWebhookTests
{
    private const string Secret = "whsec_test_secret";

    // An older API version on purpose: events from `stripe listen` may not match Stripe.net's pinned one.
    private const string Payload =
        """{"id":"evt_1","object":"event","api_version":"2024-06-20","type":"invoice.paid","data":{"object":{"id":"in_123","object":"invoice"}}}""";

    private readonly StripeBillingGateway _gateway = new(Microsoft.Extensions.Options.Options.Create(new StripeOptions()));

    [Fact]
    public void VerifyWebhook_ValidSignature_ReturnsTheEventTypeAndObjectId_EvenWithAnotherApiVersion()
    {
        var result = _gateway.VerifyWebhook(Payload, Sign(Payload, Secret), Secret);

        Assert.Equal("evt_1", result.Id);
        Assert.Equal("invoice.paid", result.Type);
        Assert.Equal("in_123", result.ObjectId);
    }

    [Fact]
    public void VerifyWebhook_TamperedBody_Throws()
    {
        var signature = Sign(Payload, Secret);

        Assert.Throws<InvalidStripeWebhookException>(() =>
            _gateway.VerifyWebhook(Payload.Replace("in_123", "in_999"), signature, Secret));
    }

    [Fact]
    public void VerifyWebhook_WrongSecret_Throws() =>
        Assert.Throws<InvalidStripeWebhookException>(() =>
            _gateway.VerifyWebhook(Payload, Sign(Payload, "whsec_other"), Secret));

    [Fact]
    public void VerifyWebhook_ExpiredTimestamp_Throws() =>
        Assert.Throws<InvalidStripeWebhookException>(() =>
            _gateway.VerifyWebhook(Payload, Sign(Payload, Secret, DateTimeOffset.UtcNow.AddHours(-1)), Secret));

    [Fact]
    public void VerifyWebhook_GarbageHeader_Throws() =>
        Assert.Throws<InvalidStripeWebhookException>(() => _gateway.VerifyWebhook(Payload, "not-a-signature", Secret));

    private static string Sign(string payload, string secret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }
}
