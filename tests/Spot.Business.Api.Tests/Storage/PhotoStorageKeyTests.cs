using Spot.Business.Api.Storage;

namespace Spot.Business.Api.Tests.Storage;

public sealed class PhotoStorageKeyTests
{
    [Theory]
    [InlineData(PhotoFormat.Jpeg, ".jpg")]
    [InlineData(PhotoFormat.Png, ".png")]
    [InlineData(PhotoFormat.Webp, ".webp")]
    public void Create_ReturnsAValidKeyWithTheFormatsExtension(PhotoFormat format, string extension)
    {
        var key = PhotoStorageKey.Create(format);

        Assert.True(PhotoStorageKey.IsValid(key));
        Assert.EndsWith(extension, key);
        Assert.Equal(format, PhotoStorageKey.GetFormat(key));
    }

    [Fact]
    public void Create_ReturnsADifferentKeyEachTime()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => PhotoStorageKey.Create(PhotoFormat.Jpeg)).ToHashSet();

        Assert.Equal(1000, keys.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f")]
    // Uppercase hex, unknown or missing extensions.
    [InlineData("3F2B9C1E8D7A4B6C9E0F1A2B3C4D5E6F.jpg")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.JPG")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpeg")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.svg")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.html")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg.php")]
    // Wrong length.
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6.jpg")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f0.jpg")]
    // Path traversal and absolute paths.
    [InlineData("../3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg")]
    [InlineData("..\\3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg")]
    [InlineData("3f/3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    // A trailing newline: .NET's "$" would accept it, the key pattern uses \z.
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg\n")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg\0")]
    [InlineData(" 3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg")]
    // Hidden temp files written by LocalPhotoStorage while saving.
    [InlineData(".3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg.0123456789abcdef0123456789abcdef.tmp")]
    // Non-ASCII digits (Arabic-Indic), which \d would accept.
    [InlineData("٣f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg")]
    public void IsValid_RejectsAnythingButAGeneratedKey(string? key)
    {
        Assert.False(PhotoStorageKey.IsValid(key));
    }

    [Fact]
    public void GetFormat_InvalidKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => PhotoStorageKey.GetFormat("../secret.jpg"));
    }
}
