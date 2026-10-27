using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Spot.Notifications.Api.Options;

/// <summary>
/// "Fcm" configuration section (#80): push delivery through Firebase Cloud Messaging HTTP v1.
/// Optional: with no <see cref="ProjectId"/> the service still boots and sending reports "not
/// configured". The service account key never lives in the repo: <see cref="CredentialsFile"/>
/// points to it — user-secrets for `dotnet run`, a read-only Docker secret from the git-ignored
/// .env otherwise (see docker-compose.yml).
/// </summary>
public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    /// <summary>Firebase project id (not a secret). Empty means push delivery isn't configured.</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>Path to the service account JSON key file, kept outside the repo.</summary>
    public string CredentialsFile { get; set; } = "";

    /// <summary>
    /// When true, FCM validates every message without delivering it (validate_only). For demos and
    /// credential checks without a real device.
    /// </summary>
    public bool ValidateOnly { get; set; }

    public string BaseUrl { get; set; } = "https://fcm.googleapis.com/v1";

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Both the project and the key file are set; whether the key is valid is only known on first use.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(CredentialsFile);
}

/// <summary>
/// Rejects malformed <see cref="FcmOptions"/>. An unconfigured section is valid (push delivery is
/// optional). Not run at startup, same as StripeOptionsValidator: only sending is affected, never
/// the device token and notification endpoints.
/// </summary>
public sealed partial class FcmOptionsValidator : IValidateOptions<FcmOptions>
{
    public ValidateOptionsResult Validate(string? name, FcmOptions options)
    {
        if (!options.IsConfigured)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        var section = FcmOptions.SectionName;

        // Firebase project ids: 6-30 lowercase letters, digits and hyphens, starting with a letter.
        if (!ProjectIdPattern().IsMatch(options.ProjectId))
            failures.Add($"{section}:{nameof(options.ProjectId)} is not a valid Firebase project id.");

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
            failures.Add($"{section}:{nameof(options.BaseUrl)} must be an absolute https URL.");

        if (options.TimeoutSeconds is < 1 or > 60)
            failures.Add($"{section}:{nameof(options.TimeoutSeconds)} must be between 1 and 60.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{4,28}[a-z0-9]$")]
    private static partial Regex ProjectIdPattern();
}
