namespace Spot.Auth.Api.Services;

/// <summary>
/// Generates opaque refresh tokens. Pure and persistence-free by design: it has no idea a
/// database exists, so it stays trivially unit-testable, and callers decide how/when to persist
/// the hash (typically atomically alongside whatever else the operation is doing).
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Generates a new random refresh token and its expiration.</summary>
    IssuedRefreshToken Issue();
}

/// <summary>
/// <paramref name="RawValue"/> is the one-time value to hand back to the client — never persist
/// it. <paramref name="HashValue"/> is what a repository should store instead, so a leaked
/// database can never be used to impersonate a session.
/// </summary>
public sealed record IssuedRefreshToken(string RawValue, string HashValue, DateTime ExpiresAt);
