namespace Spot.Auth.Api.Services;

/// <summary>
/// Computes the one-way hash of a refresh token, used both when a token is issued
/// (<c>refresh_tokens.token_hash</c> is stored, never the raw value) and when one is presented
/// back (e.g. on logout or a refresh) to look it up by that same hash.
/// </summary>
public interface IRefreshTokenHasher
{
    /// <summary>
    /// Hashes <paramref name="rawToken"/> deterministically: the same input always yields the
    /// same output, so the result can be used as an equality lookup key.
    /// </summary>
    string Hash(string rawToken);
}
