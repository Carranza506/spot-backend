using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Repositories;

public interface IDeviceTokenRepository
{
    /// <summary>
    /// Creates a row for <paramref name="token"/> if none exists yet, or claims the existing one
    /// for <paramref name="userId"/> otherwise — a token can only belong to one user at a time,
    /// so registering one already owned by someone else reassigns it rather than duplicating it.
    /// Re-registering your own already-active token is idempotent. Race-safe: if two concurrent
    /// calls both miss the same not-yet-existing row, the one that loses the database's
    /// unique-index race is retried as a claim instead of surfacing a 500.
    /// </summary>
    Task<DeviceToken> UpsertAsync(Guid userId, string token, DevicePlatform platform, CancellationToken ct = default);

    /// <summary>
    /// Loads a device token by id regardless of owner, or null if none exists — the caller is
    /// responsible for the ownership check (see <see cref="Services.IDeviceTokenService"/>), since
    /// distinguishing "doesn't exist" (404) from "exists but isn't yours" (403) requires looking
    /// it up before knowing which applies.
    /// </summary>
    Task<DeviceToken?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Marks <paramref name="token"/> inactive (soft delete) and persists the change. Idempotent:
    /// removing an already-inactive token is a no-op that still succeeds.
    /// </summary>
    Task DeactivateAsync(DeviceToken token, CancellationToken ct = default);
}
