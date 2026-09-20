using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.Repositories;

public class RefreshTokenRepository(AuthDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindActiveByHashAsync(Guid userId, string tokenHash, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Tracked (not AsNoTracking): RevokeAsync below needs the original RevokedAt value this
        // read captured — RevokedAt is a concurrency token (see AuthDbContext), so SaveChangesAsync
        // compares it against the value already in the database to make the update conditional.
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

        // Tracked (not AsNoTracking): same reasoning as the userId-scoped overload above.
        return db.RefreshTokens.FirstOrDefaultAsync(
            x => x.TokenHash == tokenHash
                && x.RevokedAt == null
                && x.ExpiresAt > now,
            ct);
    }

    /// <summary>
    /// Revokes <paramref name="token"/>, but only if it is still exactly as this <see cref="RefreshToken"/>
    /// instance was originally read (<c>RevokedAt</c> is configured as a concurrency token — see
    /// <c>AuthDbContext.OnModelCreating</c>), which is what makes this safe under concurrency: the
    /// generated <c>UPDATE</c> includes the originally-read <c>revoked_at</c> value in its
    /// <c>WHERE</c> clause, so two overlapping calls that both read the same token as active can no
    /// longer both revoke it — whichever writes second finds zero matching rows and loses the
    /// race. Returns whether this call was the one that actually revoked it.
    /// </summary>
    public async Task<bool> RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.RevokedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lost the race: someone else already changed this exact row between our read and
            // this write. Detach so this now-stale entry can't interfere with any later save on
            // the same DbContext (e.g. CreateAsync issuing the replacement pair).
            db.Entry(token).State = EntityState.Detached;
            return false;
        }
    }

    public Task CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        return db.SaveChangesAsync(ct);
    }
}
