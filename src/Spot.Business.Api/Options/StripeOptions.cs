using Microsoft.Extensions.Options;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Options;

/// <summary>
/// "Stripe" configuration section (#138). Test mode only: the secret key must be a test key. In
/// Docker every value comes from the git-ignored .env through docker-compose.yml; for `dotnet run`,
/// from user-secrets. Never put real values in appsettings*.json — this repo is public.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Secret API key of the Stripe account, test mode (sk_test_... or a restricted rk_test_...).</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>The Stripe Price (price_...) PRO subscribes to, unless plans.stripe_price_id is set.</summary>
    public string ProPriceId { get; set; } = "";

    /// <summary>
    /// File holding the webhook signing secret (whsec_...). In Docker the stripe-cli service writes
    /// it on every start, so it's read on every webhook instead of once at startup. A relative path
    /// is resolved against the app's content root.
    /// </summary>
    public string WebhookSecretFile { get; set; } = "";

    /// <summary>Where Stripe Checkout sends the user after paying. May contain {CHECKOUT_SESSION_ID}.</summary>
    public string SuccessUrl { get; set; } = "";

    /// <summary>Where Stripe Checkout sends the user when they back out.</summary>
    public string CancelUrl { get; set; } = "";

    /// <summary>
    /// The Stripe Price to subscribe to <paramref name="plan"/>: plans.stripe_price_id when set,
    /// otherwise <see cref="ProPriceId"/> for PRO. Null for a plan that can't be bought (FREE).
    /// </summary>
    public string? PriceIdFor(Plan plan) =>
        !string.IsNullOrWhiteSpace(plan.StripePriceId) ? plan.StripePriceId
        : plan.Code == PlanCode.PRO && !string.IsNullOrWhiteSpace(ProPriceId) ? ProPriceId
        : null;
}

/// <summary>
/// Rejects a missing or unsafe <see cref="StripeOptions"/>. Not run at startup on purpose (see
/// BillingServiceCollectionExtensions): only the billing calls that need Stripe fail without it.
/// </summary>
public sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        var failures = new List<string>();
        var section = StripeOptions.SectionName;

        // Payments are Stripe test mode only (#138): a live key must never be accepted.
        if (!options.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
            && !options.SecretKey.StartsWith("rk_test_", StringComparison.Ordinal))
        {
            failures.Add($"{section}:{nameof(options.SecretKey)} must be a Stripe test-mode key (sk_test_... or rk_test_...).");
        }

        if (!options.ProPriceId.StartsWith("price_", StringComparison.Ordinal))
            failures.Add($"{section}:{nameof(options.ProPriceId)} must be a Stripe Price id (price_...).");

        if (string.IsNullOrWhiteSpace(options.WebhookSecretFile))
            failures.Add($"{section}:{nameof(options.WebhookSecretFile)} is required.");

        if (!IsHttpUrl(options.SuccessUrl))
            failures.Add($"{section}:{nameof(options.SuccessUrl)} must be an absolute http(s) URL.");
        if (!IsHttpUrl(options.CancelUrl))
            failures.Add($"{section}:{nameof(options.CancelUrl)} must be an absolute http(s) URL.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // {CHECKOUT_SESSION_ID} isn't a valid URL character, so it's swapped for a token before parsing.
    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value.Replace("{CHECKOUT_SESSION_ID}", "id", StringComparison.Ordinal), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
