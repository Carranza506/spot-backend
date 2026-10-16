using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Spot.AiSearch.Api.Data;
using Spot.AiSearch.Api.Tests.Controllers;

namespace Spot.AiSearch.Api.Tests.Data;

/// <summary>
/// Regression test for Program.cs's AiSearchDbContext registration. AddDbContext's options lambda
/// runs once per scope (i.e. per request), and anything it creates becomes part of the EF options.
/// A new NpgsqlNullNameTranslator per call made every request look like a different configuration,
/// so EF Core built a new internal service provider each time and, past twenty, threw
/// ManyServiceProvidersCreatedWarning as an error — every request after that was a 500. Same test
/// as Spot.Notifications.Api.Tests' NotificationsDbContextRegistrationTests. Nothing here connects
/// to Postgres: building the model is enough to go through EF's service provider cache.
/// </summary>
[Collection(AiSearchApiCollection.Name)]
public sealed class AiSearchDbContextRegistrationTests : IDisposable
{
    private readonly RSA _jwtKey = RSA.Create(2048);
    private readonly WebApplicationFactory<Program> _factory;

    public AiSearchDbContextRegistrationTests()
    {
        // Program.cs reads Jwt:*, Gemini:ApiKey and the connection string eagerly, before
        // WebApplicationFactory's ConfigureWebHost hooks run, so they go in as environment
        // variables (see Spot.Notifications.Api.Tests' NotificationsApiFactory). None of them is
        // ever used for a real call here.
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://api.spot.cr");
        Environment.SetEnvironmentVariable("Jwt__Audience", "spot-clients");
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", _jwtKey.ExportSubjectPublicKeyInfoPem());
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "unused");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection", "Host=localhost;Database=unused;Username=unused;Password=unused");

        _factory = new WebApplicationFactory<Program>();
    }

    [Fact]
    public void ResolvingTheContextFromManyScopes_DoesNotBuildAServiceProviderPerScope()
    {
        // More than the 20 internal service providers EF Core allows before it throws.
        for (var i = 0; i < 25; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AiSearchDbContext>();

            Assert.NotNull(context.Model);
        }
    }

    public void Dispose()
    {
        _factory.Dispose();
        _jwtKey.Dispose();
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", null);
        Environment.SetEnvironmentVariable("Gemini__ApiKey", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
    }
}
