using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Spot.Shared.Auth;

/// <summary>
/// Reusable JWT Bearer validation for every microservice that trusts tokens issued by
/// Spot.Auth.Api. Validates the access token's signature (RS256, public key only — this
/// extension never needs or accepts a private key), issuer, audience, and lifetime.
/// </summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Registers JWT Bearer authentication configured from the <c>Jwt</c> section of
    /// <paramref name="configuration"/> (see <see cref="JwtOptions"/>). Call this once from a
    /// microservice's composition root, then use the standard ASP.NET Core
    /// <c>app.UseAuthentication()</c> / <c>app.UseAuthorization()</c> / <c>[Authorize]</c> pieces
    /// as usual.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown immediately (at startup, not on first request) if the Issuer, Audience, or
    /// PublicKeyPem configuration is missing or the public key is not valid PEM.
    /// </exception>
    public static IServiceCollection AddSpotJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
        Validate(jwtOptions);

        var publicKey = RSA.Create();
        try
        {
            publicKey.ImportFromPem(jwtOptions.PublicKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or FormatException)
        {
            publicKey.Dispose();
            throw new InvalidOperationException(
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.PublicKeyPem)} is not a valid PEM-encoded RSA public key.", ex);
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearerOptions =>
            {
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(publicKey),
                    // Explicit, small skew: tolerate minor clock drift between services without
                    // meaningfully extending how long an expired token stays acceptable.
                    ClockSkew = TimeSpan.FromSeconds(45),
                };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Fails fast, with an actionable message, if the options required to validate a token are
    /// missing — never logging the key material itself.
    /// </summary>
    private static void Validate(JwtOptions options)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer)) missing.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}");
        if (string.IsNullOrWhiteSpace(options.Audience)) missing.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}");
        if (string.IsNullOrWhiteSpace(options.PublicKeyPem)) missing.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.PublicKeyPem)}");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "AddSpotJwtAuthentication is missing required configuration: " + string.Join(", ", missing) +
                ". Set the public key via user-secrets in development or the Jwt__PublicKeyPem " +
                "environment variable in production (see README.md > \"RSA keys for JWT\").");
        }
    }
}
