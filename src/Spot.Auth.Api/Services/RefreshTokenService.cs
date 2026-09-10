using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Spot.Auth.Api.Configuration;

namespace Spot.Auth.Api.Services;

public sealed class RefreshTokenService(IOptions<RefreshTokenOptions> options) : IRefreshTokenService
{
    // 256 bits of entropy, base64url-encoded — large enough that guessing is infeasible and
    // short enough to fit comfortably in a header or JSON field.
    private const int TokenSizeBytes = 32;

    public IssuedRefreshToken Issue()
    {
        var rawValue = GenerateRawValue();
        var expiresAt = DateTime.UtcNow.AddDays(options.Value.ExpirationDays);

        return new IssuedRefreshToken(rawValue, Hash(rawValue), expiresAt);
    }

    private static string GenerateRawValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// SHA-256 of the raw token, hex-encoded. One-way: even a full database leak cannot be
    /// turned back into a usable refresh token.
    /// </summary>
    private static string Hash(string rawValue) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawValue)));
}
