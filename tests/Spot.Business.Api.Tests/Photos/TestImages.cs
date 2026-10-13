using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Spot.Business.Api.Tests.Photos;

/// <summary>Builds image files for the processor tests, including hand-crafted malicious ones.</summary>
internal static class TestImages
{
    /// <summary>A text marker planted in metadata; it must never show up in a processed photo.</summary>
    public const string SecretMarker = "SPOT-SECRET-GPS-MARKER";

    /// <summary>An image whose left half is red and right half is blue, so orientation can be checked.</summary>
    public static byte[] Encode(SKEncodedImageFormat format, int width, int height, bool transparent = false)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
            if (!transparent)
            {
                using var red = new SKPaint { Color = SKColors.Red };
                using var blue = new SKPaint { Color = SKColors.Blue };
                canvas.DrawRect(0, 0, width / 2f, height, red);
                canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
            }
        }

        using var data = bitmap.Encode(format, 95);
        return data.ToArray();
    }

    /// <summary>
    /// Inserts an EXIF APP1 segment right after the JPEG's SOI marker, with an Orientation tag and a
    /// Make tag holding <see cref="SecretMarker"/> (standing in for GPS coordinates).
    /// </summary>
    public static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        var marker = Encoding.ASCII.GetBytes(SecretMarker + "\0");

        // TIFF header (little-endian) + IFD0 with 2 entries, sorted by tag as the spec requires.
        const int ifdOffset = 8;
        const int entryCount = 2;
        const int ifdSize = 2 + entryCount * 12 + 4;
        const int dataOffset = ifdOffset + ifdSize;
        var tiff = new byte[dataOffset + marker.Length];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), ifdOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(ifdOffset), entryCount);

        var entry = ifdOffset + 2;
        // 0x010F Make, ASCII, stored at dataOffset.
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry), 0x010F);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry + 2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 4), (uint)marker.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 8), dataOffset);
        entry += 12;
        // 0x0112 Orientation, SHORT, inline value.
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry + 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry + 8), orientation);
        // Next IFD offset: 0 (already zero).
        marker.CopyTo(tiff, dataOffset);

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);

        return jpeg[..2].Concat(segment).Concat(jpeg[2..]).ToArray();
    }

    /// <summary>
    /// Rewrites the dimensions in the JPEG's frame header (SOF0) without adding any pixel data, like
    /// a decompression bomb that claims a huge size.
    /// </summary>
    public static byte[] WithFakeJpegDimensions(byte[] jpeg, ushort width, ushort height)
    {
        var patched = (byte[])jpeg.Clone();
        for (var i = 2; i < patched.Length - 9; i++)
        {
            if (patched[i] == 0xFF && patched[i + 1] == 0xC0)
            {
                // FF C0, length (2), precision (1), height (2), width (2).
                BinaryPrimitives.WriteUInt16BigEndian(patched.AsSpan(i + 5), height);
                BinaryPrimitives.WriteUInt16BigEndian(patched.AsSpan(i + 7), width);
                return patched;
            }
        }

        throw new InvalidOperationException("No SOF0 marker found in the test JPEG.");
    }

    /// <summary>A valid 1x1 GIF: a real image, but not an accepted format.</summary>
    public static readonly byte[] Gif = Convert.FromHexString(
        "47494638396101000100800000000000FFFFFF21F90401000000002C00000000010001000002024401003B");

    /// <summary>A valid 1x1, 24-bit BMP.</summary>
    public static byte[] Bmp()
    {
        var bmp = new byte[58];
        "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), 58);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(34), 4);
        bmp[54] = 0xFF; // one blue pixel + padding
        return bmp;
    }

    public static bool Contains(byte[] haystack, string text) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;
}
