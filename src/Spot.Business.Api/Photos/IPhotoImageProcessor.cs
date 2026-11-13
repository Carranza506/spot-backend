using Spot.Business.Api.Storage;

namespace Spot.Business.Api.Photos;

/// <summary>A photo ready to be stored: re-encoded, metadata-free, and in a supported format.</summary>
/// <param name="Content">The encoded image. Only this goes to storage, never the uploaded bytes.</param>
public sealed record ProcessedPhoto(byte[] Content, PhotoFormat Format, int Width, int Height);

/// <summary>
/// Turns an untrusted upload into a safe image: detects the real format from the content, enforces
/// size and pixel limits before decoding, decodes it, applies the EXIF orientation, scales it down
/// if needed, and re-encodes it. Re-encoding drops all metadata (EXIF with GPS, XMP, ICC, comments)
/// and anything appended to or hidden in the original file.
/// </summary>
public interface IPhotoImageProcessor
{
    /// <exception cref="InvalidPhotoException">The upload isn't an acceptable photo.</exception>
    Task<ProcessedPhoto> ProcessAsync(Stream upload, CancellationToken ct = default);
}
