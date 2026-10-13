namespace Spot.Business.Api.Storage;

/// <summary>
/// Image formats accepted for business and service photos (contracts/spot-api.yaml: JPEG, PNG
/// or WebP). A stored photo always keeps the format it was uploaded in.
/// </summary>
public enum PhotoFormat
{
    Jpeg,
    Png,
    Webp,
}

public static class PhotoFormatExtensions
{
    /// <summary>File extension used in the storage key, without the dot.</summary>
    public static string FileExtension(this PhotoFormat format) => format switch
    {
        PhotoFormat.Jpeg => "jpg",
        PhotoFormat.Png => "png",
        PhotoFormat.Webp => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>MIME type the file is served with. Never taken from the client's upload.</summary>
    public static string ContentType(this PhotoFormat format) => format switch
    {
        PhotoFormat.Jpeg => "image/jpeg",
        PhotoFormat.Png => "image/png",
        PhotoFormat.Webp => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
