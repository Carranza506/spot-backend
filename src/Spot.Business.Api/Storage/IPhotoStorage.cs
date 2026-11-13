namespace Spot.Business.Api.Storage;

/// <summary>
/// Where photo files live. Only the storage key is kept in the database (storage_key), so the
/// backend can be swapped (local volume today, an object store later) without touching the rows.
/// </summary>
/// <remarks>
/// Implementations never trust a key from outside: <see cref="SaveAsync"/> generates it, and every
/// other method validates it with <see cref="PhotoStorageKey.IsValid"/>. The content passed to
/// <see cref="SaveAsync"/> is expected to be an already validated and re-encoded image.
/// </remarks>
public interface IPhotoStorage
{
    /// <summary>
    /// Stores <paramref name="content"/> under a new key for <paramref name="format"/> and returns
    /// that key. The file only becomes visible once it's fully written.
    /// </summary>
    Task<string> SaveAsync(Stream content, PhotoFormat format, CancellationToken ct = default);

    /// <summary>
    /// Opens the file stored under <paramref name="key"/> for reading, or returns null if there's
    /// no such file or <paramref name="key"/> isn't a valid key. The caller disposes the stream.
    /// </summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes the file stored under <paramref name="key"/>. Idempotent: a missing file is not an error.</summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> isn't a valid key.</exception>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>The public, absolute URL clients use to fetch the file (<c>Photo.url</c>).</summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> isn't a valid key.</exception>
    Uri GetPublicUrl(string key);
}
