using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Spot.Shared.Auth;

namespace Spot.Shared.Tests.Auth;

/// <summary>
/// End-to-end tests that exercise <see cref="JwtAuthenticationExtensions.AddSpotJwtAuthentication"/>
/// through a real TestServer request pipeline, hitting a minimal `[Authorize]`-protected endpoint
/// exactly like a real microservice would.
/// </summary>
public sealed class JwtAuthenticationExtensionsTests : IDisposable
{
    private const string Issuer = "https://api.spot.cr";
    private const string Audience = "spot-clients";
    private const string UserId = "3fa85f64-5717-4562-b3fc-2c963f66afa6";

    // The key pair the server is configured to trust (public half only, per JwtOptions.PublicKeyPem).
    private readonly RSA _trustedKey = RSA.Create(2048);
    private readonly IHost _host;
    private readonly TestServer _server;

    public JwtAuthenticationExtensionsTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:PublicKeyPem"] = _trustedKey.ExportSubjectPublicKeyInfoPem(),
            })
            .Build();

        _host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSpotJwtAuthentication(configuration);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/protected", (ClaimsPrincipal user) =>
                                Results.Ok(new
                                {
                                    sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                                    name = user.Identity?.Name,
                                    role = user.FindFirst("role")?.Value,
                                    isInClientRole = user.IsInRole("CLIENT"),
                                }))
                            .RequireAuthorization();
                    });
                });
            })
            .Start();

        _server = _host.GetTestServer();
    }

    public void Dispose()
    {
        _host.Dispose();
        _trustedKey.Dispose();
    }

    [Fact]
    public async Task ValidToken_PassesValidation_AndExposesRealClaimValues()
    {
        var client = CreateAuthenticatedClient(CreateToken(_trustedKey, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30)));

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Guards against MapInboundClaims silently remapping "sub"/"role" to ASP.NET Core's
        // legacy claim URIs, which leaves FindFirst(Sub)/Identity.Name/role checks returning
        // null instead of throwing — a passing status code alone would not catch that.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(UserId, body.GetProperty("sub").GetString());
        Assert.Equal(UserId, body.GetProperty("name").GetString());
        Assert.Equal("CLIENT", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("isInClientRole").GetBoolean());
    }

    [Fact]
    public async Task ExpiredToken_Returns401WithUnauthorizedBody()
    {
        var client = CreateAuthenticatedClient(CreateToken(_trustedKey, DateTime.UtcNow.AddMinutes(-30), DateTime.UtcNow.AddMinutes(-5)));

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnauthorizedBodyAsync(response);
    }

    [Fact]
    public async Task TamperedSignature_Returns401WithUnauthorizedBody()
    {
        // Signed with a key the server does NOT trust — same as a token whose signature was altered.
        using var untrustedKey = RSA.Create(2048);
        var client = CreateAuthenticatedClient(CreateToken(untrustedKey, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30)));

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnauthorizedBodyAsync(response);
    }

    [Fact]
    public async Task MissingToken_Returns401WithUnauthorizedBody()
    {
        var client = _server.CreateClient();

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnauthorizedBodyAsync(response);
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertUnauthorizedBodyAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHORIZED", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.True(body.TryGetProperty("timestamp", out _));
    }

    private static string CreateToken(RSA signingKey, DateTime notBefore, DateTime expires)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(signingKey), SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, UserId),
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
}
