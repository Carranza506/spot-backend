using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
                // Without this, ASP.NET Core silently remaps standard JWT claim types to long
                // legacy URIs when building ClaimsPrincipal (sub -> the WS-Identity nameidentifier
                // URI, role -> a Microsoft role URI). That leaves user.FindFirst(Sub) / Identity.Name
                // returning null with no exception, so every consumer must be told the real claim
                // types explicitly below instead.
                bearerOptions.MapInboundClaims = false;

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
                    // Matches the claim types JwtTokenService actually issues (see RoleClaimType
                    // there, and the "sub" claim), now that MapInboundClaims is disabled.
                    RoleClaimType = "role",
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };

                bearerOptions.Events = new JwtBearerEvents
                {
                    OnChallenge = OnChallengeAsync,
                };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Replaces ASP.NET Core's default empty 401 (missing token, invalid signature, or expired
    /// token — anything that fails authentication and reaches a challenge) with a body matching
    /// the <c>Unauthorized</c> response (<c>Error</c> schema: code/message/timestamp) documented
    /// in contracts/spot-api.yaml.
    /// </summary>
    private static Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        // Prevents the default handler from also writing to the response / setting WWW-Authenticate
        // headers with its own (contract-incompatible) shape.
        context.HandleResponse();

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var message = context.AuthenticateFailure is SecurityTokenExpiredException
            ? "Tu sesión expiró, por favor inicia sesión nuevamente."
            : "Token de acceso inválido o ausente.";

        return context.Response.WriteAsJsonAsync(new
        {
            code = "UNAUTHORIZED",
            message,
            timestamp = DateTime.UtcNow,
        });
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
