using Spot.Business.Api.Options;

namespace Spot.Business.Api.Tests.Photos;

public sealed class PhotoProcessingOptionsValidatorTests
{
    private readonly PhotoProcessingOptionsValidator _validator = new();

    [Fact]
    public void Validate_Defaults_Succeed()
    {
        Assert.True(_validator.Validate(null, new PhotoProcessingOptions()).Succeeded);
    }

    public static TheoryData<string, PhotoProcessingOptions> OutOfRange => new()
    {
        { "MaxBytes", new PhotoProcessingOptions { MaxBytes = 0 } },
        { "MaxBytes", new PhotoProcessingOptions { MaxBytes = 51L * 1024 * 1024 } },
        { "MaxPixels", new PhotoProcessingOptions { MaxPixels = 0 } },
        { "MaxPixels", new PhotoProcessingOptions { MaxPixels = 100_000_001 } },
        { "MaxDimension", new PhotoProcessingOptions { MaxDimension = 15 } },
        { "MaxDimension", new PhotoProcessingOptions { MaxDimension = 8193 } },
        { "Quality", new PhotoProcessingOptions { Quality = 0 } },
        { "Quality", new PhotoProcessingOptions { Quality = 101 } },
        { "MaxConcurrentProcessing", new PhotoProcessingOptions { MaxConcurrentProcessing = 0 } },
        { "MaxConcurrentProcessing", new PhotoProcessingOptions { MaxConcurrentProcessing = 17 } },
    };

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void Validate_OutOfRange_FailsNamingTheSetting(string setting, PhotoProcessingOptions options)
    {
        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(setting));
    }
}
