namespace Spot.Shared.Auth;

/// <summary>
/// Configuration for RS256 JWT issuing (Spot.Auth.Api) and validation (every microservice).
/// </summary>
/// <remarks>
/// Bound from the <c>Jwt</c> configuration section. The values themselves are supplied by
/// whatever <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> source is wired up
/// for the running environment (appsettings.json for non-sensitive values, user-secrets in
/// development for the keys, environment variables in production) — this class and the code
/// that consumes it never need to know where a value came from. See the "RSA keys" section in
/// the README for how to generate and load the keys locally.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>The configuration section name this type binds to.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Expected/issued <c>iss</c> claim.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Expected/issued <c>aud</c> claim.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Access token lifetime in minutes. Must match the <c>expiresIn</c> (seconds) reported in
    /// the <c>AuthTokens</c> schema of contracts/spot-api.yaml — i.e. ExpiresInMinutes * 60.
    /// </summary>
    public int ExpiresInMinutes { get; set; }

    /// <summary>
    /// RSA private key (PEM, PKCS#1 or PKCS#8), used only by Spot.Auth.Api to sign access
    /// tokens. Never populated, read, or logged by any other microservice. Not versioned:
    /// provide it via user-secrets in development or an environment variable in production.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// RSA public key (PEM), used by every microservice to validate the signature of tokens
    /// issued by Spot.Auth.Api. Not secret, but still sourced from configuration (rather than
    /// hardcoded) so it can be rotated without a code change.
    /// </summary>
    public string? PublicKeyPem { get; set; }
}
