using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Spot.Notifications.Api.Repositories;
using Spot.Notifications.Api.Tests.Fakes;

namespace Spot.Notifications.Api.Tests.Controllers;

/// <summary>
/// Runs the real Spot.Notifications.Api pipeline (routing, model validation,
/// ConfigureApiBehaviorOptions, JWT auth, the global exception handler, and the REAL
/// DeviceTokenService) with only IDeviceTokenRepository swapped for an in-memory fake — same
/// shape as Spot.Auth.Api.Tests' AuthApiFactory, and for the same reason: this exercises the
/// actual service logic, not just HTTP plumbing.
/// </summary>
public sealed class NotificationsApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://api.spot.cr";
    public const string Audience = "spot-clients";

    public FakeDeviceTokenRepository DeviceTokenRepository { get; } = new();

    // Notifications.Api only ever validates tokens (AddSpotJwtAuthentication), never signs them,
    // but this fixture still needs the private half to mint test tokens the app will accept.
    private readonly RSA _jwtKey = RSA.Create(2048);

    public NotificationsApiFactory()
    {
        // NOT done via ConfigureWebHost's ConfigureAppConfiguration — see Spot.Auth.Api.Tests'
        // AuthApiFactory for the full explanation: AddSpotJwtAuthentication reads Jwt:* eagerly,
        // before builder.Build(), which is too late for that hook to reach. Environment variables
        // are read by WebApplicationBuilder.CreateBuilder() itself, so they're already in place.
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", _jwtKey.ExportSubjectPublicKeyInfoPem());

        // Same eager-read problem as the JWT settings above: Program.cs builds a
        // NpgsqlDataSource (for the Postgres enum runtime mapping) directly from
        // ConnectionStrings:DefaultConnection at the top level, before ConfigureWebHost's
        // ConfigureServices below ever runs — a missing/null connection string throws
        // ArgumentException("Host can't be null") while just constructing the data source,
        // before IDeviceTokenRepository ever gets swapped for the fake. The value itself is
        // never actually connected to: NotificationsDbContext is registered in DI but every test
        // here goes through the faked IDeviceTokenRepository instead, so this only needs to
        // parse successfully.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection", "Host=localhost;Database=unused;Username=unused;Password=unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDeviceTokenRepository>();
            services.AddSingleton<IDeviceTokenRepository>(DeviceTokenRepository);
        });
    }

    /// <summary>Mints a signed access token for <paramref name="userId"/> that the running app will accept.</summary>
    public string IssueAccessToken(Guid userId, string role = "CLIENT")
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
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }

        base.Dispose(disposing);
    }
}
