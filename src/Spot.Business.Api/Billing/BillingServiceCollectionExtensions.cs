using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Spot.Business.Api.Options;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;

namespace Spot.Business.Api.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers billing (#138): the "Stripe" options, the Stripe gateway, the webhook secret
    /// reader, and the billing repository and services.
    /// </summary>
    /// <remarks>
    /// Unlike PhotoStorage, the Stripe options are deliberately NOT validated on start: a teammate
    /// without a .env (or a test host) still gets a working API, and only the calls that need Stripe
    /// fail, with the validator's message in the log. GET /business/billing/plans and
    /// /subscription never touch Stripe.
    /// </remarks>
    public static IServiceCollection AddStripeBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName));
        services.AddSingleton<IValidateOptions<StripeOptions>, StripeOptionsValidator>();

        services.AddSingleton<IStripeBillingGateway, StripeBillingGateway>();
        services.AddSingleton<IStripeWebhookSecretProvider, FileStripeWebhookSecretProvider>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IBillingRepository, BillingRepository>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<IStripeWebhookService, StripeWebhookService>();

        return services;
    }
}
