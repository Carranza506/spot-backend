using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Spot.Shared.Auth;

namespace Spot.Auth.Api.Services;

/// <summary>
/// Issues RS256-signed access tokens using the private key configured via <see cref="JwtOptions"/>.
/// </summary>
public sealed class JwtTokenService : ITokenService, IDisposable
{
    /// <summary>Custom claim type used for the user's role, matching the contract's "role" claim.</summary>
    public const string RoleClaimType = "role";

    private readonly JwtOptions _options;
    private readonly RSA _rsa;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        Validate(_options);

        // The key is loaded once and reused for every token: Microsoft.IdentityModel caches
        // signature providers per SecurityKey/algorithm, so creating-and-disposing a new RSA
        // instance on every call risks handing that cache a disposed key on a later call.
        _rsa = CreateSigningKey(_options.PrivateKeyPem!);
        _signingCredentials = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256);
    }

    public AccessToken IssueAccessToken(string userId, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(RoleClaimType, role),
        };

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.ExpiresInMinutes);

        var token = new JwtSecurityTokenHandler().CreateJwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            subject: new ClaimsIdentity(claims),
            notBefore: now,
            expires: expires,
            issuedAt: now,
            signingCredentials: _signingCredentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var expiresInSeconds = _options.ExpiresInMinutes * 60;

        return new AccessToken(accessToken, expiresInSeconds);
    }

    public void Dispose() => _rsa.Dispose();

    /// <summary>
    /// Fails fast, with an actionable message, if the options required to sign a token are
    /// missing — never logging or including the key material itself.
    /// </summary>
    private static void Validate(JwtOptions options)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer)) missing.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}");
        if (string.IsNullOrWhiteSpace(options.Audience)) missing.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}");
        if (options.ExpiresInMinutes <= 0) missing.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.ExpiresInMinutes)} (must be > 0)");
        if (string.IsNullOrWhiteSpace(options.PrivateKeyPem)) missing.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.PrivateKeyPem)}");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "JwtTokenService is missing required configuration: " + string.Join(", ", missing) +
                ". Set the private key via user-secrets in development or the Jwt__PrivateKeyPem " +
                "environment variable in production (see README.md > \"RSA keys for JWT\").");
        }
    }

    private static RSA CreateSigningKey(string privateKeyPem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(privateKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or FormatException)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.PrivateKeyPem)} is not a valid PEM-encoded RSA private key.", ex);
        }

        return rsa;
    }
}
