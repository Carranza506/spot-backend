using Microsoft.Extensions.Options;

namespace Spot.Business.Api.Options;

/// <summary>"PhotoLimits" configuration section: how many photos a business or a service can have.</summary>
public sealed class PhotoLimitsOptions
{
    public const string SectionName = "PhotoLimits";

    /// <summary>Contract: a business can have up to 20 photos.</summary>
    public int MaxPerBusiness { get; set; } = 20;

    /// <summary>Contract: a service can have up to 10 photos.</summary>
    public int MaxPerService { get; set; } = 10;
}

/// <summary>Fails startup on out-of-range <see cref="PhotoLimitsOptions"/>.</summary>
public sealed class PhotoLimitsOptionsValidator : IValidateOptions<PhotoLimitsOptions>
{
    public ValidateOptionsResult Validate(string? name, PhotoLimitsOptions options)
    {
        var failures = new List<string>();
        var section = PhotoLimitsOptions.SectionName;

        if (options.MaxPerBusiness is < 1 or > 100)
            failures.Add($"{section}:{nameof(options.MaxPerBusiness)} must be between 1 and 100.");
        if (options.MaxPerService is < 1 or > 100)
            failures.Add($"{section}:{nameof(options.MaxPerService)} must be between 1 and 100.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
