using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Spot.Booking.Api.Repositories;
using Spot.Booking.Api.Services;
using Spot.Booking.Api.Tests.Fakes;

namespace Spot.Booking.Api.Tests.Controllers;

/// <summary>
/// The real Spot.Booking.Api pipeline and the real BookingService/AvailabilityService, with both
/// repositories swapped for in-memory fakes that share one store (a created booking is visible to
/// availability) and the clock frozen at <see cref="Now"/>. Tokens are minted the same way as
/// Spot.Business.Api.Tests' BusinessesApiFactory.
/// </summary>
public sealed class BookingsApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://api.spot.cr";
    public const string Audience = "spot-clients";

    /// <summary>2026-08-31 06:00 local (a Monday).</summary>
    public static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public FakeAvailabilityRepository ScheduleRepository { get; } = new();
    public FakeBookingRepository BookingRepository { get; }
    public FakeBookingStatusNotifier StatusNotifier { get; } = new();

    private readonly RSA _jwtKey = RSA.Create(2048);

    public BookingsApiFactory()
    {
        BookingRepository = new FakeBookingRepository(ScheduleRepository);

        // Environment variables are process-wide: see ApiFactoryCollection.
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPem", _jwtKey.ExportSubjectPublicKeyInfoPem());
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection", "Host=localhost;Database=unused;Username=unused;Password=unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAvailabilityRepository>();
            services.AddSingleton<IAvailabilityRepository>(ScheduleRepository);
            services.RemoveAll<IBookingRepository>();
            services.AddSingleton<IBookingRepository>(BookingRepository);
            services.RemoveAll<IBookingStatusNotifier>();
            services.AddSingleton<IBookingStatusNotifier>(StatusNotifier);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
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
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Serializes the test classes that spin up a WebApplicationFactory: each factory publishes its own
/// JWT public key (and connection string) through process-wide environment variables, so running
/// two in parallel would let one factory's app read the other's settings. Same as Spot.Business.Api.Tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiFactoryCollection
{
    public const string Name = "Booking.Api pipeline";
}
