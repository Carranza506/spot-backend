using System.Text;
using SkiaSharp;
using Spot.Business.Api.Options;
using Spot.Business.Api.Photos;
using Spot.Business.Api.Storage;

namespace Spot.Business.Api.Tests.Photos;

public sealed class SkiaPhotoImageProcessorTests : IDisposable
{
    private readonly SkiaPhotoImageProcessor _processor = Create(new PhotoProcessingOptions());

    public void Dispose() => _processor.Dispose();

    // ---------- Accepted formats ----------

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, PhotoFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png, PhotoFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp, PhotoFormat.Webp)]
    public async Task ProcessAsync_SupportedFormat_ReencodesInTheSameFormatAndSize(
        SKEncodedImageFormat input, PhotoFormat expected)
    {
        var upload = TestImages.Encode(input, 120, 80);

        var photo = await _processor.ProcessAsync(new MemoryStream(upload));

        Assert.Equal(expected, photo.Format);
        Assert.Equal((120, 80), (photo.Width, photo.Height));
        Assert.Equal((120, 80), DecodeSize(photo.Content));
        Assert.Equal(input, DetectFormat(photo.Content));
    }

    [Fact]
    public async Task ProcessAsync_TransparentPng_KeepsTransparency()
    {
        var photo = await _processor.ProcessAsync(new MemoryStream(TestImages.Encode(SKEncodedImageFormat.Png, 10, 10, transparent: true)));

        using var bitmap = SKBitmap.Decode(photo.Content);
        Assert.Equal(0, bitmap.GetPixel(5, 5).Alpha);
    }

    // ---------- Metadata and hidden content ----------

    [Fact]
    public async Task ProcessAsync_JpegWithExif_StripsAllMetadata()
    {
        var upload = TestImages.WithExif(TestImages.Encode(SKEncodedImageFormat.Jpeg, 40, 20), orientation: 1);
        Assert.True(TestImages.Contains(upload, TestImages.SecretMarker)); // sanity check of the fixture

        var photo = await _processor.ProcessAsync(new MemoryStream(upload));

        Assert.False(TestImages.Contains(photo.Content, TestImages.SecretMarker));
        Assert.False(TestImages.Contains(photo.Content, "Exif"));
        Assert.False(TestImages.Contains(photo.Content, "ICC_PROFILE"));
    }

    [Fact]
    public async Task ProcessAsync_ExifOrientation6_StoresThePhotoUpright()
    {
        // 40x20 with red on the left; orientation 6 means "rotate 90° clockwise to display".
        var upload = TestImages.WithExif(TestImages.Encode(SKEncodedImageFormat.Jpeg, 40, 20), orientation: 6);

        var photo = await _processor.ProcessAsync(new MemoryStream(upload));

        Assert.Equal((20, 40), (photo.Width, photo.Height));
        using var bitmap = SKBitmap.Decode(photo.Content);
        // After the rotation the left (red) half is at the top and the blue half at the bottom.
        Assert.True(IsMostlyRed(bitmap.GetPixel(10, 5)));
        Assert.True(IsMostlyBlue(bitmap.GetPixel(10, 34)));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<?php system($_GET['c']); ?>")]
    public async Task ProcessAsync_PolyglotWithAppendedPayload_DropsThePayload(string payload)
    {
        var upload = TestImages.Encode(SKEncodedImageFormat.Jpeg, 30, 30).Concat(Encoding.ASCII.GetBytes(payload)).ToArray();

        var photo = await _processor.ProcessAsync(new MemoryStream(upload));

        Assert.False(TestImages.Contains(photo.Content, payload));
    }

    // ---------- Rejected input ----------

    public static TheoryData<string, byte[]> UnsupportedUploads => new()
    {
        { "gif", TestImages.Gif },
        { "bmp", TestImages.Bmp() },
        { "svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "html", Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>hi</body></html>") },
        { "pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ\n1 0 obj\n<<>>\nendobj\n") },
        { "random", Enumerable.Range(0, 4096).Select(i => (byte)(i * 31 % 251)).ToArray() },
    };

    [Theory]
    [MemberData(nameof(UnsupportedUploads))]
    public async Task ProcessAsync_NotAJpegPngOrWebp_IsRejected(string name, byte[] upload)
    {
        _ = name; // shown in the test explorer only
        var ex = await Assert.ThrowsAsync<InvalidPhotoException>(() => _processor.ProcessAsync(new MemoryStream(upload)));

        Assert.Equal(InvalidPhotoReason.UnsupportedFormat, ex.Reason);
    }

    [Fact]
    public async Task ProcessAsync_EmptyUpload_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidPhotoException>(() => _processor.ProcessAsync(new MemoryStream()));

        Assert.Equal(InvalidPhotoReason.UnsupportedFormat, ex.Reason);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task ProcessAsync_TruncatedImage_IsRejectedAsCorrupt(SKEncodedImageFormat format)
    {
        var image = TestImages.Encode(format, 200, 200);
        var truncated = image[..(image.Length / 2)];

        var ex = await Assert.ThrowsAsync<InvalidPhotoException>(() => _processor.ProcessAsync(new MemoryStream(truncated)));

        Assert.Contains(ex.Reason, new[] { InvalidPhotoReason.Corrupt, InvalidPhotoReason.UnsupportedFormat });
    }

    [Fact]
    public async Task ProcessAsync_HeaderClaimingTooManyPixels_IsRejectedBeforeDecoding()
    {
        // A tiny file whose header says 10000x10000 (100 MP > 40 MP).
        var bomb = TestImages.WithFakeJpegDimensions(TestImages.Encode(SKEncodedImageFormat.Jpeg, 8, 8), 10_000, 10_000);

        var ex = await Assert.ThrowsAsync<InvalidPhotoException>(() => _processor.ProcessAsync(new MemoryStream(bomb)));

        Assert.Equal(InvalidPhotoReason.TooManyPixels, ex.Reason);
    }

    [Fact]
    public async Task ProcessAsync_MoreBytesThanTheLimit_IsRejected()
    {
        using var processor = Create(new PhotoProcessingOptions { MaxBytes = 1_000 });

        var ex = await Assert.ThrowsAsync<InvalidPhotoException>(
            () => processor.ProcessAsync(new MemoryStream(new byte[1_001])));

        Assert.Equal(InvalidPhotoReason.TooLarge, ex.Reason);
    }

    [Fact]
    public async Task ProcessAsync_EndlessUpload_StopsReadingRightAfterTheLimit()
    {
        using var processor = Create(new PhotoProcessingOptions { MaxBytes = 100_000 });
        var upload = new EndlessStream();

        await Assert.ThrowsAsync<InvalidPhotoException>(() => processor.ProcessAsync(upload));

        // At most one read chunk past the limit, never the whole (infinite) stream.
        Assert.InRange(upload.BytesRead, 100_001, 100_000 + 81_920);
    }

    [Fact]
    public async Task ProcessAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.ProcessAsync(
            new MemoryStream(TestImages.Encode(SKEncodedImageFormat.Png, 10, 10)), cts.Token));
    }

    // ---------- Scaling ----------

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task ProcessAsync_LargerThanMaxDimension_IsScaledDownKeepingTheAspectRatio(SKEncodedImageFormat format)
    {
        using var processor = Create(new PhotoProcessingOptions { MaxDimension = 300 });

        var photo = await processor.ProcessAsync(new MemoryStream(TestImages.Encode(format, 900, 450)));

        Assert.True(Math.Max(photo.Width, photo.Height) <= 300);
        Assert.InRange((double)photo.Width / photo.Height, 1.95, 2.05);
        Assert.Equal((photo.Width, photo.Height), DecodeSize(photo.Content));
    }

    // ---------- Helpers ----------

    private static SkiaPhotoImageProcessor Create(PhotoProcessingOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options));

    private static (int, int) DecodeSize(byte[] image)
    {
        using var codec = SKCodec.Create(SKData.CreateCopy(image));
        return (codec.Info.Width, codec.Info.Height);
    }

    private static SKEncodedImageFormat DetectFormat(byte[] image)
    {
        using var codec = SKCodec.Create(SKData.CreateCopy(image));
        return codec.EncodedFormat;
    }

    private static bool IsMostlyRed(SKColor c) => c.Red > 180 && c.Blue < 90;
    private static bool IsMostlyBlue(SKColor c) => c.Blue > 180 && c.Red < 90;

    /// <summary>A stream that never ends, counting how much was read from it.</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            BytesRead += count;
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
