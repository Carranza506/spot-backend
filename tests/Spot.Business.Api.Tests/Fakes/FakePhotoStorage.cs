using System.Collections.Concurrent;
using Spot.Business.Api.Storage;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IPhotoStorage. Generates and validates keys exactly like the real one
/// (PhotoStorageKey), so callers see the same URLs and the same rejections. <see cref="FailDeletes"/>
/// simulates a file that can't be removed.
/// </summary>
public sealed class FakePhotoStorage : IPhotoStorage
{
    public const string PublicBaseUrl = "https://api.spot.cr/api/v1/business/media/photos/";

    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public bool FailDeletes { get; set; }

    public void Reset()
    {
        _files.Clear();
        FailDeletes = false;
    }

    public async Task<string> SaveAsync(Stream content, PhotoFormat format, CancellationToken ct = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        var key = PhotoStorageKey.Create(format);
        _files[key] = copy.ToArray();
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(
            PhotoStorageKey.IsValid(key) && _files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!PhotoStorageKey.IsValid(key))
            throw new ArgumentException("Not a valid photo storage key.", nameof(key));
        if (FailDeletes)
            throw new IOException("Simulated storage failure.");

        _files.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Uri GetPublicUrl(string key)
    {
        if (!PhotoStorageKey.IsValid(key))
            throw new ArgumentException("Not a valid photo storage key.", nameof(key));

        return new Uri(new Uri(PublicBaseUrl), key);
    }
}
