using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spot.Booking.Api.Repositories;
using Spot.Booking.Api.Tests.Fakes;

namespace Spot.Booking.Api.Tests.Controllers;

/// <summary>
/// The real Spot.Booking.Api pipeline and the real AvailabilityService, with IAvailabilityRepository
/// swapped for an in-memory fake and the clock frozen at <see cref="Now"/>. Same shape as
/// Spot.Business.Api.Tests' BusinessesApiFactory; no JWT setup, since availability is public.
/// </summary>
public sealed class AvailabilityApiFactory : WebApplicationFactory<Program>
{
    /// <summary>2026-08-31 06:00 local: before every date the tests query, so no slot is "in the past" unless a test says so.</summary>
    public static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public FakeAvailabilityRepository Repository { get; } = new();

    public AvailabilityApiFactory()
    {
        // Placeholder for Program.cs's eagerly-built NpgsqlDataSource — only parsed, never
        // connected to, since the repository is faked. See BusinessesApiFactory.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection", "Host=localhost;Database=unused;Username=unused;Password=unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAvailabilityRepository>();
            services.AddSingleton<IAvailabilityRepository>(Repository);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);

        base.Dispose(disposing);
    }
}
