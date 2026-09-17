using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Tests.Fakes;

namespace Spot.Business.Api.Tests.Controllers;

/// <summary>
/// Runs the real Spot.Business.Api pipeline (routing, model validation,
/// ConfigureApiBehaviorOptions, JWT auth, the global exception handler, and the REAL
/// CategoryService) with only ICategoryRepository swapped for an in-memory fake — same shape as
/// Spot.Auth.Api.Tests' AuthApiFactory, and for the same reason: this exercises the actual
/// business-rule logic in CategoryService, not just HTTP plumbing.
/// </summary>
public sealed class CategoriesApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://api.spot.cr";
    public const string Audience = "spot-clients";

    public FakeCategoryRepository CategoryRepository { get; } = new();

    // Business.Api only ever validates tokens (AddSpotJwtAuthentication), never signs them, but
    // this fixture still needs the private half to mint test tokens the app will accept.
    private readonly RSA _jwtKey = RSA.Create(2048);

    public CategoriesApiFactory()
    {
        // NOT done via ConfigureWebHost's ConfigureAppConfiguration — see Spot.Auth.Api.Tests'
        // AuthApiFactory for the full explanation: AddSpotJwtAuthentication reads Jwt:* eagerly,
        // before builder.Build(), which is too late for that hook to reach. Environment variables
        // are read by WebApplicationBuilder.CreateBuilder() itself, so they're already in place.
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", _jwtKey.ExportSubjectPublicKeyInfoPem());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICategoryRepository>();
            services.AddSingleton<ICategoryRepository>(CategoryRepository);
        });
    }

    /// <summary>Mints a signed access token for <paramref name="userId"/>/<paramref name="role"/> that the running app will accept.</summary>
    public string IssueAccessToken(Guid userId, string role)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(_jwtKey), SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("role", role),
        };
        var now = DateTime.UtcNow;

        var token = new JwtSecurityTokenHandler().CreateJwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            subject: new ClaimsIdentity(claims),
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(30),
            issuedAt: now,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _jwtKey.Dispose();
            Environment.SetEnvironmentVariable("Jwt__Issuer", null);
            Environment.SetEnvironmentVariable("Jwt__Audience", null);
            Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", null);
        }

        base.Dispose(disposing);
    }
}
