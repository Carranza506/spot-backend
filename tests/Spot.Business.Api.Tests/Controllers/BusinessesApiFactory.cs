using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Spot.Business.Api.Billing;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Storage;
using Spot.Business.Api.Tests.Fakes;

namespace Spot.Business.Api.Tests.Controllers;

/// <summary>
/// Same shape as <see cref="CategoriesApiFactory"/>: the real Spot.Business.Api pipeline and the
/// REAL BusinessService, with IBusinessRepository AND ICategoryRepository swapped for in-memory
/// fakes — BusinessService now depends on both (it validates categoryIds against
/// ICategoryRepository for PUT .../categories), and only ICategoryRepository was already
/// registered against a real Postgres-backed CategoryRepository in Program.cs.
/// IServiceOfferingRepository is swapped too, so ServicesControllerTests can share this factory:
/// ServiceOfferingService checks ownership through BusinessService, so through the same IBusinessRepository.
/// </summary>
public sealed class BusinessesApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://api.spot.cr";
    public const string Audience = "spot-clients";

    public FakeBusinessRepository BusinessRepository { get; } = new();
    public FakeCategoryRepository CategoryRepository { get; } = new();
    public FakeServiceOfferingRepository ServiceRepository { get; } = new();

    /// <summary>Contacts (#59) live under a business, so their tests share this factory's businesses.</summary>
    public FakeBusinessContactRepository ContactRepository { get; } = new();

    /// <summary>Schedule exceptions (#61) also live under a business.</summary>
    public FakeBusinessScheduleExceptionRepository ScheduleExceptionRepository { get; } = new();

    /// <summary>Reviews (#70): a business's reviews are listed only while it's visible, so they share its businesses.</summary>
    public FakeReviewRepository ReviewRepository { get; } = new();

    /// <summary>
    /// Photos (#65): rows and files are faked (no database, nothing written to disk), but the real
    /// PhotoService and the real SkiaSharp image processing run, so uploads are decoded for real.
    /// </summary>
    public FakePhotoRepository PhotoRepository { get; } = new();
    public FakePhotoStorage PhotoStorage { get; } = new();

    /// <summary>
    /// Billing (#138): plans, subscriptions and payments in memory, and a fake Stripe (no network).
    /// The real BillingService and StripeWebhookService run on top of them.
    /// </summary>
    public FakeBillingRepository BillingRepository { get; } = new();
    public FakeStripeBillingGateway StripeGateway { get; } = new();

    /// <summary>The Stripe price the test host is configured with (Stripe__ProPriceId).</summary>
    public const string ProPriceId = "price_pro_test";

    private readonly RSA _jwtKey = RSA.Create(2048);

    public BusinessesApiFactory()
    {
        // Environment variables, not ConfigureAppConfiguration — see CategoriesApiFactory. Because
        // they're process-wide, every test class using one of these factories is in the
        // ApiFactoryCollection, so two factories never overwrite each other's key mid-run.
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", _jwtKey.ExportSubjectPublicKeyInfoPem());

        // Placeholder connection string for Program.cs's eagerly-built NpgsqlDataSource — see
        // CategoriesApiFactory. Only parsed, never connected to: both repositories are faked.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection", "Host=localhost;Database=unused;Username=unused;Password=unused");

        // Placeholder Stripe settings (#138), only so StripeOptionsValidator passes: Stripe itself
        // and the webhook secret file are faked. Not a real key.
        Environment.SetEnvironmentVariable("Stripe__SecretKey", "sk_test_placeholder");
        Environment.SetEnvironmentVariable("Stripe__ProPriceId", ProPriceId);
        Environment.SetEnvironmentVariable("Stripe__WebhookSecretFile", "unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBusinessRepository>();
            services.AddSingleton<IBusinessRepository>(BusinessRepository);
            services.RemoveAll<ICategoryRepository>();
            services.AddSingleton<ICategoryRepository>(CategoryRepository);
            services.RemoveAll<IServiceOfferingRepository>();
            services.AddSingleton<IServiceOfferingRepository>(ServiceRepository);
            services.RemoveAll<IBusinessContactRepository>();
            services.AddSingleton<IBusinessContactRepository>(ContactRepository);
            services.RemoveAll<IBusinessScheduleExceptionRepository>();
            services.AddSingleton<IBusinessScheduleExceptionRepository>(ScheduleExceptionRepository);
            services.RemoveAll<IReviewRepository>();
            services.AddSingleton<IReviewRepository>(ReviewRepository);
            services.RemoveAll<IPhotoRepository>();
            services.AddSingleton<IPhotoRepository>(PhotoRepository);
            services.RemoveAll<IPhotoStorage>();
            services.AddSingleton<IPhotoStorage>(PhotoStorage);
            services.RemoveAll<IBillingRepository>();
            services.AddSingleton<IBillingRepository>(BillingRepository);
            services.RemoveAll<IStripeBillingGateway>();
            services.AddSingleton<IStripeBillingGateway>(StripeGateway);
            services.RemoveAll<IStripeWebhookSecretProvider>();
            services.AddSingleton<IStripeWebhookSecretProvider, FakeStripeWebhookSecretProvider>();
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
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("Stripe__SecretKey", null);
            Environment.SetEnvironmentVariable("Stripe__ProPriceId", null);
            Environment.SetEnvironmentVariable("Stripe__WebhookSecretFile", null);
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
