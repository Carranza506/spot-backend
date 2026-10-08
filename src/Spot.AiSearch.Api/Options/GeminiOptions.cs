namespace Spot.AiSearch.Api.Options;

/// <summary>
/// Configuration for calling the Gemini API. Bound from the <c>Gemini</c> configuration section,
/// same pattern as <see cref="Spot.Shared.Auth.JwtOptions"/>: non-secret values live in
/// appsettings.Development.json, <see cref="ApiKey"/> is supplied via user-secrets in development
/// or the Gemini__ApiKey environment variable in production — never hardcoded or committed.
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-3.6-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public int TimeoutSeconds { get; set; } = 10;
}
