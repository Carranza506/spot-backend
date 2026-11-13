using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Spot.Auth.Api.Configuration;

namespace Spot.Auth.Api.Services;

public sealed class RefreshTokenIssuer(IOptions<RefreshTokenOptions> options, IRefreshTokenHasher hasher) : IRefreshTokenIssuer
{
    // 256 bits of entropy, base64url-encoded — large enough that guessing is infeasible and
    // short enough to fit comfortably in a header or JSON field.
    private const int TokenSizeBytes = 32;

    public IssuedRefreshToken Issue()
    {
        var rawValue = GenerateRawValue();
        var expiresAt = DateTime.UtcNow.AddDays(options.Value.ExpirationDays);

        // Reuses IRefreshTokenHasher (the same hasher RefreshTokenService uses to look up a
        // token on logout) rather than hashing inline here, so issuing and revoking a token can
        // never silently disagree on how it's hashed.
        return new IssuedRefreshToken(rawValue, hasher.Hash(rawValue), expiresAt);
    }

    private static string GenerateRawValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
