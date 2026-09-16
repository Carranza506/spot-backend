using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Spot.Auth.Api.Configuration;
using Spot.Auth.Api.Services;
using Spot.Shared.Auth;

namespace Spot.Auth.Api.Tests.Controllers;

/// <summary>
/// End-to-end tests for <c>POST /auth/logout</c> through a real request pipeline (routing, JWT
/// auth, model binding, the controller) — same <see cref="TestServer"/> approach as
/// Spot.Shared.Tests' JwtAuthenticationExtensionsTests, so the [Authorize] behavior itself isn't
/// re-tested here. <see cref="IRefreshTokenService"/> is faked so these tests stay about the
/// HTTP/contract layer; RefreshTokenService's own logic is covered by RefreshTokenServiceTests.
/// </summary>
public sealed class AuthControllerTests : IDisposable
{
    private const string Issuer = "https://api.spot.cr";
    private const string Audience = "spot-clients";

    private readonly RSA _signingKey = RSA.Create(2048);
    private readonly FakeRefreshTokenService _refreshTokenService = new();
    private readonly IHost _host;
    private readonly TestServer _server;

    public AuthControllerTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:PublicKeyPem"] = _signingKey.ExportSubjectPublicKeyInfoPem(),
            })
            .Build();

        _host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    // ConfigureSpotApiErrorShape() is the exact same call Program.cs makes —
                    // shared on purpose, so this test host and the real app can't silently
                    // drift into answering 400s with two different bodies.
                    //
                    // AddApplicationPart is required here specifically because this test host
                    // (unlike the real app) doesn't run from the Spot.Auth.Api assembly, so
                    // AddControllers()'s default assembly scan would never find AuthController
                    // and every request below would 404 instead of exercising real behavior.
                    services.AddControllers()
                        .AddApplicationPart(typeof(Spot.Auth.Api.Controllers.AuthController).Assembly)
                        .ConfigureSpotApiErrorShape();
                    services.AddSpotJwtAuthentication(configuration);
                    services.AddSingleton<IRefreshTokenService>(_refreshTokenService);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Start();

        _server = _host.GetTestServer();
    }

    public void Dispose()
    {
        _host.Dispose();
        _signingKey.Dispose();
    }

    [Fact]
    public async Task Logout_ValidTokenAndBody_Returns204_AndRevokesForTheCallingUser()
    {
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync("/auth/logout", new { refreshToken = "some-refresh-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(userId, _refreshTokenService.LastCalledWithUserId);
        Assert.Equal("some-refresh-token", _refreshTokenService.LastCalledWithRawToken);
    }

    [Fact]
    public async Task Logout_NoAccessToken_Returns401_AndDoesNotCallTheService()
    {
        var client = _server.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/logout", new { refreshToken = "some-refresh-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(_refreshTokenService.WasCalled);
    }

    [Fact]
    public async Task Logout_MissingRefreshToken_Returns400WithErrorBody()
    {
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/auth/logout", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.False(_refreshTokenService.WasCalled);
    }

    [Fact]
    public async Task Logout_ExpiredAccessToken_Returns401_AndDoesNotCallTheService()
    {
        var client = CreateAuthenticatedClient(
            Guid.NewGuid(), notBefore: DateTime.UtcNow.AddMinutes(-30), expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await client.PostAsJsonAsync("/auth/logout", new { refreshToken = "some-refresh-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(_refreshTokenService.WasCalled);
    }

    private HttpClient CreateAuthenticatedClient(Guid userId, DateTime? notBefore = null, DateTime? expires = null)
    {
        var client = _server.CreateClient();
        var token = CreateToken(userId, notBefore ?? DateTime.UtcNow.AddMinutes(-1), expires ?? DateTime.UtcNow.AddMinutes(30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string CreateToken(Guid userId, DateTime notBefore, DateTime expires)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(_signingKey), SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("role", "CLIENT"),
        };

        var token = new JwtSecurityTokenHandler().CreateJwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            subject: new ClaimsIdentity(claims),
            notBefore: notBefore,
            expires: expires,
            issuedAt: notBefore,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Records how it was called instead of touching any real persistence.</summary>
    private sealed class FakeRefreshTokenService : IRefreshTokenService
    {
        public bool WasCalled { get; private set; }
        public Guid? LastCalledWithUserId { get; private set; }
        public string? LastCalledWithRawToken { get; private set; }

        public Task RevokeAsync(Guid userId, string rawRefreshToken, CancellationToken ct = default)
        {
            WasCalled = true;
            LastCalledWithUserId = userId;
            LastCalledWithRawToken = rawRefreshToken;
            return Task.CompletedTask;
        }
    }
}
