using Spot.Business.Api.Billing;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>A fixed webhook signing secret, instead of the file stripe-cli writes.</summary>
public sealed class FakeStripeWebhookSecretProvider : IStripeWebhookSecretProvider
{
    public const string Secret = "whsec_test_fake";

    public Task<string> GetSecretAsync(CancellationToken ct = default) => Task.FromResult(Secret);
}
