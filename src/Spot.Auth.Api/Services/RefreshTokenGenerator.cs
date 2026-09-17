using System.Security.Cryptography;

namespace Spot.Auth.Api.Services;

/// <summary>
/// Generates high-entropy, URL-safe raw refresh tokens. Shared by <see cref="RefreshTokenService"/>
/// (real issuing, via rotation) and <c>DevController</c> (Development-only, to seed a token
/// there's no login flow yet to issue one for).
/// </summary>
public static class RefreshTokenGenerator
{
    /// <summary>256 bits of randomness, matching the entropy assumption in the XML doc on
    /// <see cref="Sha256RefreshTokenHasher"/>.</summary>
    public static string GenerateRaw() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
