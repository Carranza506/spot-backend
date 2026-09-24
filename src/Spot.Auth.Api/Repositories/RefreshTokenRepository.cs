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

    public Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.RevokedAt = DateTimeOffset.UtcNow;
        return db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Loaded and mutated one by one, then a single SaveChangesAsync — not
        // ExecuteUpdateAsync, which the InMemory provider used by this suite's tests doesn't
        // support, and which would also depart from RevokeAsync's own tracked-entity style above.
        var activeTokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
            token.RevokedAt = now;

        await db.SaveChangesAsync(ct);
    }
}
