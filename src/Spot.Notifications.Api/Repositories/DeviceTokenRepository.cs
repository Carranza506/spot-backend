using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Notifications.Api.Data;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Repositories;

/// <remarks>
/// Not covered by an InMemory-backed repository test suite (unlike e.g. Spot.Auth.Api's
/// RefreshTokenRepository): NotificationsDbContext's existing AuditLog.OldValues/NewValues
/// (JsonDocument, mapped to jsonb) make EF's InMemory provider fail model validation for the
/// whole context — a pre-existing constraint, not specific to DeviceToken. Covered instead by
/// DeviceTokenServiceTests/DeviceTokensControllerTests (fake-backed) and verified against a real
/// local Postgres, including the enum mapping and the unique-index race safety net below.
/// </remarks>
public sealed class DeviceTokenRepository(NotificationsDbContext db) : IDeviceTokenRepository
{
    public async Task<DeviceToken> UpsertAsync(
        Guid userId, string token, DevicePlatform platform, CancellationToken ct = default)
    {
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(x => x.Token == token, ct);

        if (existing is not null)
        {
            Claim(existing, userId, platform);
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var deviceToken = new DeviceToken { UserId = userId, Token = token, Platform = platform, IsActive = true };
        db.DeviceTokens.Add(deviceToken);

        try
        {
            await db.SaveChangesAsync(ct);
            return deviceToken;
        }
        catch (DbUpdateException ex) when (IsUniqueTokenViolation(ex))
        {
            // Lost the race: another request for the same token — possibly the same caller
            // firing twice, possibly a different user claiming it — committed first. Drop the
            // failed insert and apply the same claim to the row that won instead of surfacing a
            // 500 or an undocumented 409.
            db.Entry(deviceToken).State = EntityState.Detached;

            var winner = await db.DeviceTokens.FirstAsync(x => x.Token == token, ct);
            Claim(winner, userId, platform);
            await db.SaveChangesAsync(ct);
            return winner;
        }
    }

    public Task<DeviceToken?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.DeviceTokens.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task DeactivateAsync(DeviceToken token, CancellationToken ct = default)
    {
        token.IsActive = false;
        return db.SaveChangesAsync(ct);
    }

    /// <summary>Assigns the row to <paramref name="userId"/>, reactivating it regardless of who owned it before.</summary>
    private static void Claim(DeviceToken token, Guid userId, DevicePlatform platform)
    {
        token.UserId = userId;
        token.Platform = platform;
        token.IsActive = true;
        token.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsUniqueTokenViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_device_tokens_token",
        };
}
