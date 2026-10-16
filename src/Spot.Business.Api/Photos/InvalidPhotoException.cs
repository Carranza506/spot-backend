namespace Spot.Business.Api.Photos;

/// <summary>Why an uploaded file was rejected as a photo. Mapped to 400 (or 413) by the controllers.</summary>
public enum InvalidPhotoReason
{
    /// <summary>Not a JPEG, PNG or WebP, judging by the file content (not its name or declared type).</summary>
    UnsupportedFormat,

    /// <summary>A JPEG, PNG or WebP that can't be decoded completely (truncated or corrupt).</summary>
    Corrupt,

    /// <summary>Width × height above <c>PhotoProcessing:MaxPixels</c>.</summary>
    TooManyPixels,

    /// <summary>More bytes than <c>PhotoProcessing:MaxBytes</c>.</summary>
    TooLarge,
}

/// <summary>The uploaded file isn't an acceptable photo. Never carries image content or internal details.</summary>
public sealed class InvalidPhotoException(InvalidPhotoReason reason)
    : Exception($"Invalid photo upload: {reason}.")
{
    public InvalidPhotoReason Reason { get; } = reason;
}
