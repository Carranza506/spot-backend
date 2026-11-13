using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Spot.Business.Api.Options;

namespace Spot.Business.Api.Tests.Storage;

public sealed class PhotoStorageOptionsValidatorTests
{
    private const string ValidRoot = "App_Data/photos";

    [Theory]
    [InlineData("Development", "http://localhost:5195/api/v1/business/media/photos")]
    [InlineData("Development", "https://api.spot.cr/api/v1/business/media/photos")]
    [InlineData("Production", "https://api.spot.cr/api/v1/business/media/photos")]
    public void Validate_ValidOptions_Succeeds(string environment, string publicBaseUrl)
    {
        var result = Validate(environment, new PhotoStorageOptions { RootPath = ValidRoot, PublicBaseUrl = publicBaseUrl });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingRootPath_Fails(string rootPath)
    {
        var result = Validate("Development", new PhotoStorageOptions
        {
            RootPath = rootPath,
            PublicBaseUrl = "http://localhost:5195/api/v1/business/media/photos",
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("RootPath"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/api/v1/business/media/photos")]
    [InlineData("ftp://api.spot.cr/photos")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///var/lib/spot/photos")]
    [InlineData("https://api.spot.cr/photos?sig=1")]
    [InlineData("https://api.spot.cr/photos#top")]
    [InlineData("https://user:password@api.spot.cr/photos")]
    public void Validate_UnsafePublicBaseUrl_Fails(string publicBaseUrl)
    {
        var result = Validate("Development", new PhotoStorageOptions { RootPath = ValidRoot, PublicBaseUrl = publicBaseUrl });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("PublicBaseUrl"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Validate_PlainHttpOutsideDevelopment_Fails(string environment)
    {
        var result = Validate(environment, new PhotoStorageOptions
        {
            RootPath = ValidRoot,
            PublicBaseUrl = "http://api.spot.cr/api/v1/business/media/photos",
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("https"));
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string environment, PhotoStorageOptions options) =>
        new PhotoStorageOptionsValidator(new TestHostEnvironment(environment)).Validate(null, options);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Spot.Business.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
