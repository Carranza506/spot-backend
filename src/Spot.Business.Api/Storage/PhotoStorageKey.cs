using System.Text.RegularExpressions;

namespace Spot.Business.Api.Storage;

/// <summary>
/// Storage keys of photo files: a random 128-bit id plus the format's extension, e.g.
/// <c>3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg</c>. The key is also the file name at the end of
/// <c>Photo.url</c> (GET /business/media/photos/{fileName}).
/// </summary>
/// <remarks>
/// Keys are always generated here, never taken from the client, and every key that comes back
/// from a URL is checked against <see cref="IsValid"/> before it touches the file system. The
/// pattern only allows lowercase hex and one known extension, so it can't contain path
/// separators, "..", or a drive letter.
/// </remarks>
public static partial class PhotoStorageKey
{
    /// <summary>A new, unguessable key for a file in <paramref name="format"/>.</summary>
    public static string Create(PhotoFormat format) => $"{Guid.NewGuid():N}.{format.FileExtension()}";

    /// <summary>True only for keys with the exact shape <see cref="Create"/> produces.</summary>
    public static bool IsValid(string? key) => key is not null && KeyPattern().IsMatch(key);

    /// <summary>The format encoded in a valid <paramref name="key"/>'s extension.</summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> isn't a valid key.</exception>
    public static PhotoFormat GetFormat(string key)
    {
        if (!IsValid(key))
            throw new ArgumentException("Not a valid photo storage key.", nameof(key));

        return Path.GetExtension(key) switch
        {
            ".jpg" => PhotoFormat.Jpeg,
            ".png" => PhotoFormat.Png,
            _ => PhotoFormat.Webp,
        };
    }

    // Same pattern as the fileName parameter in contracts/spot-api.yaml, but anchored with \z:
    // in .NET, "$" also matches before a trailing "\n", which would let "<key>.jpg\n" through.
    // No RegexOptions.IgnoreCase on purpose (keys are always lowercase), and a match timeout as
    // a guard against pathological input, even though this pattern can't backtrack.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(?:jpg|png|webp)\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex KeyPattern();
}
