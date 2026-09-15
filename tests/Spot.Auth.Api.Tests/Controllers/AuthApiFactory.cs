using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Tests.Fakes;

namespace Spot.Auth.Api.Tests.Controllers;

/// <summary>
/// Runs the real Spot.Auth.Api pipeline (routing, model validation, ConfigureApiBehaviorOptions,
/// the global exception handler) with IUserRepository swapped for an in-memory fake, so these
/// tests never need a real Postgres database.
/// </summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public FakeUserRepository UserRepository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            using var rsa = RSA.Create(2048);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://api.spot.cr",
                ["Jwt:Audience"] = "spot-clients",
                ["Jwt:ExpiresInMinutes"] = "60",
                ["Jwt:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(UserRepository);
        });
    }
}
