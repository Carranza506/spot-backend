using Microsoft.Extensions.Options;

namespace Spot.Business.Api.Options;

/// <summary>"PhotoProcessing" configuration section: limits and output settings for uploaded photos.</summary>
public sealed class PhotoProcessingOptions
{
    public const string SectionName = "PhotoProcessing";

    /// <summary>Largest accepted upload, in bytes (contract: 5 MB).</summary>
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Largest accepted image, in pixels (width × height), checked from the header before
    /// decoding (contract: 40 megapixels). Guards against decompression bombs.
    /// </summary>
    public long MaxPixels { get; set; } = 40_000_000;

    /// <summary>Longest side of the stored image, in pixels. Larger photos are scaled down.</summary>
    public int MaxDimension { get; set; } = 2560;

    /// <summary>Encoder quality (1-100) for JPEG and WebP. PNG is lossless.</summary>
    public int Quality { get; set; } = 85;

    /// <summary>
    /// How many photos are decoded at the same time. A decoded image can take ~4 bytes per pixel
    /// (160 MB at 40 megapixels), so this keeps concurrent uploads from exhausting the container's
    /// memory. Further uploads wait for a slot.
    /// </summary>
    public int MaxConcurrentProcessing { get; set; } = 1;
}

/// <summary>Fails startup on out-of-range <see cref="PhotoProcessingOptions"/>.</summary>
public sealed class PhotoProcessingOptionsValidator : IValidateOptions<PhotoProcessingOptions>
{
    public ValidateOptionsResult Validate(string? name, PhotoProcessingOptions options)
    {
        var failures = new List<string>();
        var section = PhotoProcessingOptions.SectionName;

        // Upper bounds keep a typo in configuration from turning the limits off.
        if (options.MaxBytes is < 1 or > 50 * 1024 * 1024)
            failures.Add($"{section}:{nameof(options.MaxBytes)} must be between 1 byte and 50 MB.");
        if (options.MaxPixels is < 1 or > 100_000_000)
            failures.Add($"{section}:{nameof(options.MaxPixels)} must be between 1 and 100,000,000.");
        if (options.MaxDimension is < 16 or > 8192)
            failures.Add($"{section}:{nameof(options.MaxDimension)} must be between 16 and 8192.");
        if (options.Quality is < 1 or > 100)
            failures.Add($"{section}:{nameof(options.Quality)} must be between 1 and 100.");
        if (options.MaxConcurrentProcessing is < 1 or > 16)
            failures.Add($"{section}:{nameof(options.MaxConcurrentProcessing)} must be between 1 and 16.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
