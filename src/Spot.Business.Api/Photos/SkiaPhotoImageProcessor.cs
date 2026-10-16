using Microsoft.Extensions.Options;
using SkiaSharp;
using Spot.Business.Api.Options;
using Spot.Business.Api.Storage;

namespace Spot.Business.Api.Photos;

/// <summary>
/// <see cref="IPhotoImageProcessor"/> on SkiaSharp (the same libjpeg-turbo, libpng and libwebp
/// decoders Chrome uses). Registered as a singleton: the concurrency limit is shared by all requests.
/// </summary>
/// <remarks>
/// Pipeline, in order, so the expensive and risky parts only run on input that passed the cheap checks:
/// <list type="number">
/// <item>Read at most <c>MaxBytes</c> + 1 bytes (the controller enforces the same limit earlier).</item>
/// <item>Detect the format from the magic bytes; only JPEG, PNG and WebP pass.</item>
/// <item>Check width × height from the header against <c>MaxPixels</c>, before allocating pixels.</item>
/// <item>Decode, converting to sRGB; JPEG is decoded directly at a reduced scale when it's larger than needed.</item>
/// <item>Apply the EXIF orientation, so photos taken in portrait aren't stored sideways.</item>
/// <item>Scale down to <c>MaxDimension</c> on the longest side.</item>
/// <item>Re-encode in the original format, with no metadata at all.</item>
/// </list>
/// </remarks>
public sealed class SkiaPhotoImageProcessor : IPhotoImageProcessor, IDisposable
{
    private const int ReadChunkSize = 81_920;

    private readonly PhotoProcessingOptions _options;
    private readonly SemaphoreSlim _slots;

    public SkiaPhotoImageProcessor(IOptions<PhotoProcessingOptions> options)
    {
        _options = options.Value;
        _slots = new SemaphoreSlim(_options.MaxConcurrentProcessing, _options.MaxConcurrentProcessing);
    }

    public async Task<ProcessedPhoto> ProcessAsync(Stream upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var bytes = await ReadBoundedAsync(upload, _options.MaxBytes, ct);

        // Decoding is CPU- and memory-heavy, so only MaxConcurrentProcessing uploads do it at once.
        // A client that disconnects while waiting gives its turn up (the token is cancelled).
        await _slots.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return Process(bytes);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();

    /// <summary>
    /// Copies the upload into memory, failing as soon as it goes past <paramref name="maxBytes"/>
    /// instead of buffering the whole thing first.
    /// </summary>
    private static async Task<byte[]> ReadBoundedAsync(Stream upload, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkSize];
        long total = 0;
        int read;
        while ((read = await upload.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidPhotoException(InvalidPhotoReason.TooLarge);

            buffer.Write(chunk, 0, read);
        }

        if (total == 0)
            throw new InvalidPhotoException(InvalidPhotoReason.UnsupportedFormat);

        return buffer.ToArray();
    }

    private ProcessedPhoto Process(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        // SKCodec picks the decoder from the content's magic bytes; the file name and the
        // Content-Type the client sent are never consulted.
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidPhotoException(InvalidPhotoReason.UnsupportedFormat);

        var format = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => PhotoFormat.Jpeg,
            SKEncodedImageFormat.Png => PhotoFormat.Png,
            SKEncodedImageFormat.Webp => PhotoFormat.Webp,
            _ => throw new InvalidPhotoException(InvalidPhotoReason.UnsupportedFormat),
        };

        // Header only: no pixel memory has been allocated yet.
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            throw new InvalidPhotoException(InvalidPhotoReason.Corrupt);
        if ((long)info.Width * info.Height > _options.MaxPixels)
            throw new InvalidPhotoException(InvalidPhotoReason.TooManyPixels);

        var current = Decode(codec);
        try
        {
            current = Replace(current, ApplyOrigin(current, codec.EncodedOrigin));
            current = Replace(current, ScaleDown(current, _options.MaxDimension));

            return new ProcessedPhoto(Encode(current, format, _options.Quality), format, current.Width, current.Height);
        }
        finally
        {
            current.Dispose();
        }
    }

    private SKBitmap Decode(SKCodec codec)
    {
        var info = codec.Info;

        // Decoders that support it (JPEG: 1/8 steps) decode straight to a smaller size, which saves
        // most of the memory for big photos. Others return the full size and are scaled down later.
        var longest = Math.Max(info.Width, info.Height);
        var scale = longest > _options.MaxDimension ? (float)_options.MaxDimension / longest : 1f;
        var size = codec.GetScaledDimensions(scale);

        // Converting to sRGB here means the ICC profile can be dropped on encode without
        // changing how the colors look.
        var target = new SKImageInfo(
            size.Width,
            size.Height,
            SKColorType.Rgba8888,
            info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());

        var bitmap = new SKBitmap(target);
        // Anything but a full success (including IncompleteInput, a truncated file) is rejected,
        // rather than storing a partially decoded image.
        var result = codec.GetPixels(target, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new InvalidPhotoException(InvalidPhotoReason.Corrupt);
        }

        return bitmap;
    }

    /// <summary>
    /// Bakes the EXIF orientation into the pixels (the EXIF block itself isn't kept). Null when the
    /// image is already upright.
    /// </summary>
    private static SKBitmap? ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return null;

        var (w, h) = ((float)source.Width, (float)source.Height);
        // Maps each source pixel (x, y) to its upright position. EXIF values 5-8 swap the sides.
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),     // 2: mirror horizontally
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), // 3: rotate 180°
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // 4: mirror vertically
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),       // 5: transpose
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),     // 6: rotate 90° clockwise
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1), // 7: transverse
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),   // 8: rotate 90° counter-clockwise
            _ => SKMatrix.Identity,
        };
        if (matrix == SKMatrix.Identity)
            return null;

        var swapsSides = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var info = swapsSides
            ? source.Info.WithSize(source.Height, source.Width)
            : source.Info;

        var upright = new SKBitmap(info);
        upright.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(upright);
        canvas.SetMatrix(matrix);
        // Every orientation maps pixels onto pixels exactly, so nearest-neighbor keeps them unchanged.
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.Flush();

        return upright;
    }

    /// <summary>Scales <paramref name="source"/> so its longest side is at most <paramref name="maxDimension"/>; null if it already fits.</summary>
    private static SKBitmap? ScaleDown(SKBitmap source, int maxDimension)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxDimension)
            return null;

        var ratio = (double)maxDimension / longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(source.Height * ratio));

        return source.Resize(source.Info.WithSize(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Scaling the photo down failed.");
    }

    private static byte[] Encode(SKBitmap bitmap, PhotoFormat format, int quality)
    {
        // The pixels are already sRGB; encoding them with no color space attached means the
        // encoders don't write an ICC profile either. Nothing else from the original file is
        // carried over: SkiaSharp only encodes pixels.
        using var pixmap = new SKPixmap(bitmap.Info.WithColorSpace(null), bitmap.GetPixels(), bitmap.RowBytes);
        var skiaFormat = format switch
        {
            PhotoFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            PhotoFormat.Png => SKEncodedImageFormat.Png,
            PhotoFormat.Webp => SKEncodedImageFormat.Webp,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

        using var encoded = pixmap.Encode(skiaFormat, quality)
            ?? throw new InvalidOperationException($"Encoding the photo as {format} failed.");

        return encoded.ToArray();
    }

    /// <summary>Disposes <paramref name="current"/> when a step produced a new bitmap, and returns the one to keep.</summary>
    private static SKBitmap Replace(SKBitmap current, SKBitmap? next)
    {
        if (next is null)
            return current;

        current.Dispose();
        return next;
    }
}
