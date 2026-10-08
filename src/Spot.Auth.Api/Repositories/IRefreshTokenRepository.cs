using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.Repositories;

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Finds the refresh token owned by <paramref name="userId"/> matching <paramref name="tokenHash"/>,
    /// but only if it is still usable (not already revoked, not expired). Scoping the lookup to
    /// the owning user at the query level — rather than filtering by hash alone and checking
    /// ownership afterwards — means a valid access token for one user can never even observe,
    /// let alone revoke, another user's refresh token.
    /// </summary>
    Task<RefreshToken?> FindActiveByHashAsync(Guid userId, string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Finds the still-usable (not revoked, not expired) refresh token matching
    /// <paramref name="tokenHash"/>, without scoping to a known owner. Used by <c>POST
    /// /auth/refresh</c>, which — unlike logout — is called with only the refresh token itself
    /// (no access token/JWT to read a caller id from), so the owner isn't known until after the
    /// lookup.
    /// </summary>
    Task<RefreshToken?> FindActiveByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Atomically marks <paramref name="token"/> as revoked (sets <c>revoked_at</c>), but only if
    /// it is still active. Returns <c>true</c> if this call revoked it, or <c>false</c> if it had
    /// already been revoked by the time this write ran (e.g. a concurrent caller won the race) —
    /// callers that must enforce single-use tokens (like refresh rotation) need to check this.
    /// </summary>
    Task<bool> RevokeAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Persists a newly issued <paramref name="token"/>.
    /// </summary>
    Task CreateAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Revokes every currently-active refresh token belonging to <paramref name="userId"/> —
    /// used by change-password to end every other session, on every device, the moment the
    /// password changes. Already-revoked or expired tokens are left untouched.
    /// </summary>
    Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct = default);
}
