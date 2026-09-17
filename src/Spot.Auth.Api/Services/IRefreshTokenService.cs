using Spot.Auth.Api.DTOs;

namespace Spot.Auth.Api.Services;

/// <summary>
/// Business logic for refresh tokens that spans hashing and persistence — the layer a
/// controller talks to instead of the repository directly.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Revokes the refresh token identified by <paramref name="rawRefreshToken"/>, if it belongs
    /// to <paramref name="userId"/> and is still active (see <see cref="IRefreshTokenRepository"/>).
    /// </summary>
    /// <remarks>
    /// Deliberately does not report whether a matching token was found: the token might not
    /// exist, might belong to someone else, or might already be revoked/expired — from the
    /// caller's point of view (already holding a valid access token) the outcome is the same
    /// either way, "this session is not active". Distinguishing those cases in the response
    /// would let a caller use logout to probe for valid-but-not-theirs refresh tokens.
    /// </remarks>
    Task RevokeAsync(Guid userId, string rawRefreshToken, CancellationToken ct = default);

    /// <summary>
    /// Rotates <paramref name="rawRefreshToken"/> for a new access/refresh token pair: the given
    /// token is revoked and a freshly issued one takes its place, so a stolen-and-reused refresh
    /// token cannot be replayed after its legitimate owner has refreshed past it.
    /// </summary>
    /// <returns>
    /// The new token pair, or <see langword="null"/> if <paramref name="rawRefreshToken"/> is
    /// not an active token (unknown, already revoked, expired) or names a user that no longer
    /// exists.
    /// </returns>
    Task<AuthTokensDto?> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);
}
