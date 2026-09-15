using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Services;

public class RefreshTokenService(IRefreshTokenRepository repository, IRefreshTokenHasher hasher) : IRefreshTokenService
{
    public async Task RevokeAsync(Guid userId, string rawRefreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var tokenHash = hasher.Hash(rawRefreshToken);
        var token = await repository.FindActiveByHashAsync(userId, tokenHash, ct);

        // No matching active token for this user: nothing to revoke. Not an error — see
        // IRefreshTokenService.RevokeAsync's remarks on why this stays silent either way.
        if (token is null)
            return;

        await repository.RevokeAsync(token, ct);
    }
}
