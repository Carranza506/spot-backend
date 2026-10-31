using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Spot.AiSearch.Api.Repositories;
using Spot.AiSearch.Api.Services.Business;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.AiSearch.Api.Tests.Fakes;

namespace Spot.AiSearch.Api.Tests.Controllers;

/// <summary>
/// Runs the real Spot.AiSearch.Api pipeline (routing, JWT auth and roles, the real
/// AiRequestService) with only IAiRequestRepository swapped for the in-memory fake — same shape as
/// Spot.Notifications.Api.Tests' NotificationsApiFactory.
/// </summary>
public sealed class AiSearchApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://api.spot.cr";
    public const string Audience = "spot-clients";

    public FakeAiRequestRepository Repository { get; } = new();

    /// <summary>Replaces the Gemini API (#73's POST /ai-search/query); the requests endpoints never call it.</summary>
    public FakeGeminiClient Gemini { get; } = new();

    /// <summary>Replaces the HTTP call to Spot.Business.Api (#73).</summary>
    public FakeBusinessSearchClient BusinessSearch { get; } = new();

    // The app only validates tokens; the private half is here to mint test tokens it will accept.
    private readonly RSA _jwtKey = RSA.Create(2048);

    public AiSearchApiFactory()
    {
        // Program.cs reads these eagerly, before ConfigureWebHost runs, so they go in as
        // environment variables (see NotificationsApiFactory). Postgres and Gemini are never called.
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", _jwtKey.ExportSubjectPublicKeyInfoPem());
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "unused");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection", "Host=localhost;Database=unused;Username=unused;Password=unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiRequestRepository>();
            services.AddSingleton<IAiRequestRepository>(Repository);
            services.RemoveAll<IGeminiClient>();
            services.AddSingleton<IGeminiClient>(Gemini);
            services.RemoveAll<IBusinessSearchClient>();
            services.AddSingleton<IBusinessSearchClient>(BusinessSearch);
        });
    }

    /// <summary>Mints a signed access token with <paramref name="role"/> that the running app will accept.</summary>
    public string IssueAccessToken(string role)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(_jwtKey), SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
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
            Environment.SetEnvironmentVariable("Gemini__ApiKey", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Serializes the test classes that boot the app: each one publishes its settings (JWT public key
/// included) through process-wide environment variables, so two in parallel would clash.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AiSearchApiCollection
{
    public const string Name = "AiSearch.Api pipeline";
}
