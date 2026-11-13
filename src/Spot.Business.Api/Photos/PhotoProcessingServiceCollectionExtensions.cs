using Microsoft.Extensions.Options;
using Spot.Business.Api.Options;

namespace Spot.Business.Api.Photos;

public static class PhotoProcessingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SkiaPhotoImageProcessor"/> as a singleton <see cref="IPhotoImageProcessor"/>
    /// (its concurrency limit must be shared), bound to the "PhotoProcessing" section and validated
    /// when the app starts.
    /// </summary>
    public static IServiceCollection AddPhotoImageProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PhotoProcessingOptions>()
            .Bind(configuration.GetSection(PhotoProcessingOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PhotoProcessingOptions>, PhotoProcessingOptionsValidator>();
        services.AddSingleton<IPhotoImageProcessor, SkiaPhotoImageProcessor>();

        return services;
    }
}
