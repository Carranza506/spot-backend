using Microsoft.Extensions.Options;
using Spot.Notifications.Api.Options;

namespace Spot.Notifications.Api.Services.Push;

public static class PushServiceCollectionExtensions
{
    /// <summary>
    /// Registers push delivery through FCM HTTP v1 (#80): the "Fcm" options, the access token
    /// provider, the sender and the per-user push service.
    /// </summary>
    /// <remarks>
    /// Same idea as Spot.Business.Api's AddStripeBilling: the options are NOT validated at startup,
    /// so a teammate without Firebase credentials (or a test host) still gets a working service, and
    /// only sending reports "not configured".
    /// </remarks>
    public static IServiceCollection AddFcmPush(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FcmOptions>()
            .Bind(configuration.GetSection(FcmOptions.SectionName));
        services.AddSingleton<IValidateOptions<FcmOptions>, FcmOptionsValidator>();

        services.AddSingleton<IFcmAccessTokenProvider, GoogleFcmAccessTokenProvider>();
        services.AddHttpClient<IPushSender, FcmPushSender>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();

        return services;
    }
}
