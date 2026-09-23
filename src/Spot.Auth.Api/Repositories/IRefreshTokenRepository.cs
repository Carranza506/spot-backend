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
    /// Marks <paramref name="token"/> as revoked (sets <c>revoked_at</c>) and persists the change.
    /// </summary>
    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Revokes every currently-active refresh token belonging to <paramref name="userId"/> —
    /// used by change-password to end every other session, on every device, the moment the
    /// password changes. Already-revoked or expired tokens are left untouched.
    /// </summary>
    Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct = default);
}
