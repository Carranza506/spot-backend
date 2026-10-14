using Microsoft.Extensions.Options;
using Spot.Business.Api.Options;

namespace Spot.Business.Api.Storage;

/// <summary>
/// <see cref="IPhotoStorage"/> on the local file system (a Docker named volume in compose).
/// Files are spread over subdirectories named after the key's first two characters, so no single
/// directory grows unbounded: <c>{RootPath}/3f/3f2b9c1e….jpg</c>.
/// </summary>
/// <remarks>
/// Writes go to a temporary file in the same directory and are then renamed into place, so a
/// reader never sees a half-written photo and a failed upload leaves nothing behind. On Linux,
/// new files are owner-only (0600) and new directories 0700: only the app's user can read them.
/// </remarks>
public sealed class LocalPhotoStorage : IPhotoStorage
{
    private const int BufferSize = 81_920;

    private readonly string _root;
    private readonly Uri _publicBaseUrl;

    public LocalPhotoStorage(IOptions<PhotoStorageOptions> options)
    {
        var value = options.Value;
        // Trailing separator so the "is it inside the root?" check below can't be fooled by a
        // sibling directory sharing the prefix (/data/photos vs /data/photos-old).
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.RootPath)) + Path.DirectorySeparatorChar;
        // Trailing slash so new Uri(base, key) appends the key instead of replacing the last segment.
        _publicBaseUrl = new Uri(value.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public async Task<string> SaveAsync(Stream content, PhotoFormat format, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var key = PhotoStorageKey.Create(format);
        var finalPath = ResolvePath(key);
        var directory = Path.GetDirectoryName(finalPath)!;
        // The root first: Directory.CreateDirectory only applies the mode to the last directory, so
        // creating the shard alone would leave an app-created root at the default 755 (local runs;
        // in Docker the root already exists as 700).
        CreateDirectory(_root);
        CreateDirectory(directory);

        // Starts with "." and isn't a valid key, so it can never be served or deleted by key.
        var tempPath = Path.Combine(directory, $".{key}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(tempPath, NewFileOptions()))
            {
                await content.CopyToAsync(file, BufferSize, ct);
                await file.FlushAsync(ct);
            }

            // overwrite: false — a key collision (128 random bits) fails loudly instead of
            // silently replacing someone else's photo.
            File.Move(tempPath, finalPath, overwrite: false);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        if (!PhotoStorageKey.IsValid(key))
            return Task.FromResult<Stream?>(null);

        try
        {
            Stream stream = new FileStream(
                ResolvePath(key),
                FileMode.Open,
                FileAccess.Read,
                // FileShare.Delete: a concurrent delete of the photo doesn't fail on Windows while
                // the file is being served; the open handle keeps reading the old content.
                FileShare.Read | FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // The shard directory was never created: nothing to delete.
        }

        return Task.CompletedTask;
    }

    public Uri GetPublicUrl(string key)
    {
        if (!PhotoStorageKey.IsValid(key))
            throw new ArgumentException("Not a valid photo storage key.", nameof(key));

        return new Uri(_publicBaseUrl, key);
    }

    /// <summary>
    /// Full path of <paramref name="key"/>'s file. The key pattern already rules out traversal;
    /// the root check is defense in depth in case that pattern is ever loosened.
    /// </summary>
    private string ResolvePath(string key)
    {
        if (!PhotoStorageKey.IsValid(key))
            throw new ArgumentException("Not a valid photo storage key.", nameof(key));

        var path = Path.GetFullPath(Path.Combine(_root, key[..2], key));
        if (!path.StartsWith(_root, StringComparison.Ordinal))
            throw new ArgumentException("Photo storage key resolves outside the storage root.", nameof(key));

        return path;
    }

    private static FileStreamOptions NewFileOptions()
    {
        var options = new FileStreamOptions
        {
            // CreateNew: never truncate or reuse an existing file.
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = BufferSize,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        return options;
    }

    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a leftover ".tmp" file is never served (not a valid key).
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
