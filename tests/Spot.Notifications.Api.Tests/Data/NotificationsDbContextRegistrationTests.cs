using Microsoft.Extensions.DependencyInjection;
using Spot.Notifications.Api.Data;
using Spot.Notifications.Api.Tests.Controllers;

namespace Spot.Notifications.Api.Tests.Data;

/// <summary>
/// Regression test for Program.cs's NotificationsDbContext registration. AddDbContext's options
/// lambda runs once per scope (i.e. per request), and anything it creates becomes part of the EF
/// options. A new NpgsqlSnakeCaseNameTranslator per call made every request look like a different
/// configuration, so EF Core built a new internal service provider each time and, past twenty,
/// threw ManyServiceProvidersCreatedWarning as an error — every request after that was a 500.
/// The controller tests can't see this: NotificationsApiFactory swaps the repository for a fake,
/// so nothing there ever resolves the DbContext. Nothing here connects to Postgres either:
/// building the model is enough to go through EF's service provider cache.
/// </summary>
[Collection(NotificationsApiCollection.Name)]
public class NotificationsDbContextRegistrationTests(NotificationsApiFactory factory) : IClassFixture<NotificationsApiFactory>
{
    [Fact]
    public void ResolvingTheContextFromManyScopes_DoesNotBuildAServiceProviderPerScope()
    {
        // More than the 20 internal service providers EF Core allows before it throws.
        for (var i = 0; i < 25; i++)
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            Assert.NotNull(context.Model);
        }
    }
}
