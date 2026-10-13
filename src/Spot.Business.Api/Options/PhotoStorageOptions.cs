using Microsoft.Extensions.Options;

namespace Spot.Business.Api.Options;

/// <summary>"PhotoStorage" configuration section: where photo files are kept and how they're reached.</summary>
public sealed class PhotoStorageOptions
{
    public const string SectionName = "PhotoStorage";

    /// <summary>
    /// Directory the photo files are written to. A relative path is resolved against the app's
    /// content root. In Docker it points at a named volume, so files survive container rebuilds.
    /// </summary>
    public string RootPath { get; set; } = "";

    /// <summary>
    /// Absolute base URL of GET /business/media/photos/{fileName} as clients see it (through the
    /// API Gateway). The storage key is appended to build <c>Photo.url</c>.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";
}

/// <summary>
/// Fails startup on a missing or unsafe <see cref="PhotoStorageOptions"/> instead of on the first
/// upload. Outside Development the public URL must be HTTPS, so photo URLs never downgrade a page.
/// </summary>
public sealed class PhotoStorageOptionsValidator(IHostEnvironment environment) : IValidateOptions<PhotoStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, PhotoStorageOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RootPath))
            failures.Add($"{PhotoStorageOptions.SectionName}:{nameof(options.RootPath)} is required.");
        else if (options.RootPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            failures.Add($"{PhotoStorageOptions.SectionName}:{nameof(options.RootPath)} contains invalid characters.");

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{PhotoStorageOptions.SectionName}:{nameof(options.PublicBaseUrl)} must be an absolute http(s) URL.");
        }
        else
        {
            if (!string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment))
                failures.Add($"{PhotoStorageOptions.SectionName}:{nameof(options.PublicBaseUrl)} can't have a query or fragment.");
            if (!string.IsNullOrEmpty(baseUrl.UserInfo))
                failures.Add($"{PhotoStorageOptions.SectionName}:{nameof(options.PublicBaseUrl)} can't contain credentials.");
            if (baseUrl.Scheme != Uri.UriSchemeHttps && !environment.IsDevelopment())
                failures.Add($"{PhotoStorageOptions.SectionName}:{nameof(options.PublicBaseUrl)} must use https outside Development.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
