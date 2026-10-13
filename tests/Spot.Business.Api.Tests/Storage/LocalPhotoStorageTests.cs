using Spot.Business.Api.Options;
using Spot.Business.Api.Storage;

namespace Spot.Business.Api.Tests.Storage;

/// <summary>Real file system round-trips in a throwaway directory per test.</summary>
public sealed class LocalPhotoStorageTests : IDisposable
{
    private const string PublicBaseUrl = "https://api.spot.cr/api/v1/business/media/photos";

    // Each test gets its own parent directory too, because the traversal test plants a file next
    // to the root.
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "spot-photo-tests", Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly LocalPhotoStorage _storage;

    public LocalPhotoStorageTests()
    {
        _root = Path.Combine(_testDirectory, "photos");
        _storage = new LocalPhotoStorage(Microsoft.Extensions.Options.Options.Create(
            new PhotoStorageOptions { RootPath = _root, PublicBaseUrl = PublicBaseUrl }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_ReturnsTheSameBytes()
    {
        var bytes = RandomBytes(100_000);

        var key = await _storage.SaveAsync(new MemoryStream(bytes), PhotoFormat.Png);

        Assert.True(PhotoStorageKey.IsValid(key));
        Assert.EndsWith(".png", key);
        await using var stream = await _storage.OpenReadAsync(key);
        Assert.NotNull(stream);
        Assert.Equal(bytes, await ReadAllAsync(stream!));
    }

    [Fact]
    public async Task SaveAsync_WritesUnderAShardDirectoryInsideTheRoot_AndLeavesNoTempFiles()
    {
        var key = await _storage.SaveAsync(new MemoryStream(RandomBytes(10)), PhotoFormat.Jpeg);

        var file = Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(_root, key[..2], key), file);
    }

    [Fact]
    public async Task SaveAsync_ContentFailsMidway_LeavesNoFileBehind()
    {
        var content = new FailingStream(failAfterBytes: 1_000);

        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(content, PhotoFormat.Jpeg));

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SaveAsync_Cancelled_LeavesNoFileBehind()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _storage.SaveAsync(new MemoryStream(RandomBytes(10)), PhotoFormat.Jpeg, cts.Token));

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OpenReadAsync_UnknownKey_ReturnsNull()
    {
        Assert.Null(await _storage.OpenReadAsync(PhotoStorageKey.Create(PhotoFormat.Webp)));
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public async Task OpenReadAsync_InvalidKey_ReturnsNullWithoutTouchingTheFileSystem(string key)
    {
        // A real file right outside the root, which a traversal would reach.
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "..", "appsettings.json"), "secret");

        Assert.Null(await _storage.OpenReadAsync(key));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFile_AndIsIdempotent()
    {
        var key = await _storage.SaveAsync(new MemoryStream(RandomBytes(10)), PhotoFormat.Jpeg);

        await _storage.DeleteAsync(key);
        await _storage.DeleteAsync(key);

        Assert.Null(await _storage.OpenReadAsync(key));
    }

    [Fact]
    public async Task DeleteAsync_KeyWhoseShardDirectoryDoesNotExist_DoesNotThrow()
    {
        await _storage.DeleteAsync(PhotoStorageKey.Create(PhotoFormat.Jpeg));
    }

    [Fact]
    public async Task DeleteAsync_InvalidKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.DeleteAsync("../outside.jpg"));
    }

    [Theory]
    [InlineData(PublicBaseUrl)]
    [InlineData(PublicBaseUrl + "/")]
    public void GetPublicUrl_AppendsTheKeyToTheBaseUrl(string baseUrl)
    {
        var storage = new LocalPhotoStorage(Microsoft.Extensions.Options.Options.Create(
            new PhotoStorageOptions { RootPath = _root, PublicBaseUrl = baseUrl }));
        var key = PhotoStorageKey.Create(PhotoFormat.Jpeg);

        var url = storage.GetPublicUrl(key);

        Assert.Equal($"{PublicBaseUrl}/{key}", url.ToString());
    }

    [Fact]
    public void GetPublicUrl_InvalidKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => _storage.GetPublicUrl("../x.jpg"));
    }

    [Fact]
    public async Task SaveAsync_OnUnix_CreatesOwnerOnlyFiles()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix permissions don't apply; CI runs on Linux, where this test is meaningful.

        var key = await _storage.SaveAsync(new MemoryStream(RandomBytes(10)), PhotoFormat.Jpeg);

        var path = Path.Combine(_root, key[..2], key);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(path)!));
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    /// <summary>Returns bytes until <c>failAfterBytes</c>, then throws, like a dropped upload.</summary>
    private sealed class FailingStream(int failAfterBytes) : Stream
    {
        private int _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= failAfterBytes)
                throw new IOException("Simulated broken upload.");

            var n = Math.Min(count, failAfterBytes - _read);
            _read += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
