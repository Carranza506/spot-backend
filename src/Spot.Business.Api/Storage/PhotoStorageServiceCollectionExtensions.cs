using Microsoft.Extensions.Options;
using Spot.Business.Api.Options;

namespace Spot.Business.Api.Storage;

public static class PhotoStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LocalPhotoStorage"/> as <see cref="IPhotoStorage"/>, bound to the
    /// "PhotoStorage" section and validated when the app starts (not on the first upload).
    /// </summary>
    public static IServiceCollection AddLocalPhotoStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PhotoStorageOptions>()
            .Bind(configuration.GetSection(PhotoStorageOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                // A relative RootPath (local `dotnet run`) is resolved against the content root,
                // not the process's working directory, which depends on how the app was started.
                if (!string.IsNullOrWhiteSpace(options.RootPath) && !Path.IsPathRooted(options.RootPath))
                    options.RootPath = Path.Combine(environment.ContentRootPath, options.RootPath);
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PhotoStorageOptions>, PhotoStorageOptionsValidator>();
        services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();

        return services;
    }
}
