using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;
using Spot.Auth.Api.Tests.Fakes;

namespace Spot.Auth.Api.Tests.Controllers;

/// <summary>
/// Runs the real Spot.Auth.Api pipeline (routing, model validation, ConfigureApiBehaviorOptions,
/// JWT auth, the global exception handler) with IUserRepository and IRefreshTokenService swapped
/// for in-memory fakes, so these tests never need a real Postgres database.
/// </summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://api.spot.cr";
    public const string Audience = "spot-clients";

    public FakeUserRepository UserRepository { get; } = new();
    public FakeRefreshTokenService RefreshTokenService { get; } = new();

    // One key pair for the whole fixture, used both to configure the app's own JWT signing
    // (Jwt:PrivateKeyPem) and validation (Jwt:PublicKeyPem, via AddSpotJwtAuthentication) — and
    // exposed below so tests can mint their own access tokens (e.g. for [Authorize] endpoints
    // like POST /auth/logout) that the running app will actually accept.
    private readonly RSA _jwtKey = RSA.Create(2048);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:ExpiresInMinutes"] = "60",
                ["Jwt:PrivateKeyPem"] = _jwtKey.ExportRSAPrivateKeyPem(),
                ["Jwt:PublicKeyPem"] = _jwtKey.ExportSubjectPublicKeyInfoPem(),
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(UserRepository);

            services.RemoveAll<IRefreshTokenService>();
            services.AddSingleton<IRefreshTokenService>(RefreshTokenService);
        });
    }

    /// <summary>
    /// Builds a signed access token for <paramref name="userId"/> — same shape (claim types,
    /// issuer, audience) as JwtTokenService actually issues — so tests can call an
    /// <c>[Authorize]</c> endpoint as a specific authenticated user without going through
    /// POST /auth/register or /login first. Valid for 30 minutes unless <paramref name="notBefore"/>/
    /// <paramref name="expires"/> are overridden (e.g. to build an already-expired token).
    /// </summary>
    public string IssueAccessToken(Guid userId, string role = "CLIENT", DateTime? notBefore = null, DateTime? expires = null)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(_jwtKey), SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("role", role),
        };
        var effectiveNotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1);

        var token = new JwtSecurityTokenHandler().CreateJwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            subject: new ClaimsIdentity(claims),
            notBefore: effectiveNotBefore,
            expires: expires ?? DateTime.UtcNow.AddMinutes(30),
            issuedAt: effectiveNotBefore,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _jwtKey.Dispose();

        base.Dispose(disposing);
    }
}
