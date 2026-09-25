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
/// Same shape as <see cref="CategoriesApiFactory"/>: the real Spot.Business.Api pipeline and the
/// REAL BusinessService, with only IBusinessRepository swapped for an in-memory fake.
/// </summary>
public sealed class BusinessesApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://api.spot.cr";
    public const string Audience = "spot-clients";

    public FakeBusinessRepository BusinessRepository { get; } = new();

    private readonly RSA _jwtKey = RSA.Create(2048);

    public BusinessesApiFactory()
    {
        // Environment variables, not ConfigureAppConfiguration — see CategoriesApiFactory. Because
        // they're process-wide, every test class using one of these factories is in the
        // ApiFactoryCollection, so two factories never overwrite each other's key mid-run.
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", _jwtKey.ExportSubjectPublicKeyInfoPem());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBusinessRepository>();
            services.AddSingleton<IBusinessRepository>(BusinessRepository);
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

/// <summary>
/// Serializes the test classes that spin up a WebApplicationFactory: each factory publishes its
/// own JWT public key through process-wide environment variables, so running two in parallel
/// would let one factory's app validate against the other's key.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiFactoryCollection
{
    public const string Name = "Business.Api pipeline";
}
