using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.Repositories;

public class RefreshTokenRepository(AuthDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindActiveByHashAsync(Guid userId, string tokenHash, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Tracked (not AsNoTracking): the only caller of this method immediately revokes what
        // it finds, and EF needs to track the entity for that update to be saved.
        return db.RefreshTokens.FirstOrDefaultAsync(
            x => x.UserId == userId
                && x.TokenHash == tokenHash
                && x.RevokedAt == null
                && x.ExpiresAt > now,
            ct);
    }

    public Task<RefreshToken?> FindActiveByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Tracked (not AsNoTracking): same reasoning as the userId-scoped overload above — the
        // caller (RefreshTokenService.RefreshAsync) immediately revokes what it finds.
        return db.RefreshTokens.FirstOrDefaultAsync(
            x => x.TokenHash == tokenHash
                && x.RevokedAt == null
                && x.ExpiresAt > now,
            ct);
    }

    public Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.RevokedAt = DateTimeOffset.UtcNow;
        return db.SaveChangesAsync(ct);
    }

    public Task CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        return db.SaveChangesAsync(ct);
    }
}
