using Microsoft.Extensions.Options;
using Spot.Business.Api.Options;

namespace Spot.Business.Api.Billing;

/// <summary>The current webhook signing secret (whsec_...).</summary>
public interface IStripeWebhookSecretProvider
{
    /// <exception cref="InvalidOperationException">No secret available (file missing or empty).</exception>
    Task<string> GetSecretAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads <see cref="StripeOptions.WebhookSecretFile"/> on every call, same idea as
/// JWT_PUBLIC_KEY_FILE but never cached: the stripe-cli service rewrites the file each time it
/// starts, and may do so before or after business-api, so a value read once at startup could be
/// missing or stale. The file is a few dozen bytes and webhooks are rare, so this costs nothing.
/// </summary>
public sealed class FileStripeWebhookSecretProvider(IOptions<StripeOptions> options, IHostEnvironment environment)
    : IStripeWebhookSecretProvider
{
    public async Task<string> GetSecretAsync(CancellationToken ct = default)
    {
        var path = options.Value.WebhookSecretFile;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(environment.ContentRootPath, path);

        var secret = File.Exists(path) ? (await File.ReadAllTextAsync(path, ct)).Trim() : "";
        if (secret.Length == 0)
            throw new InvalidOperationException(
                $"The Stripe webhook signing secret file '{path}' is missing or empty. Is the stripe-cli service (or `stripe listen`) running?");

        return secret;
    }
}
