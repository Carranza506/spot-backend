using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;

namespace Spot.Notifications.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IDeviceTokenRepository — no database involved. Mirrors the real
/// repository's claim-by-token behavior so callers see the same outcome regardless of whether
/// they went through the fake or the real, Postgres-backed one.
/// </summary>
public sealed class FakeDeviceTokenRepository : IDeviceTokenRepository
{
    private readonly Dictionary<Guid, DeviceToken> _tokens = [];

    public void Reset() => _tokens.Clear();

    /// <summary>Seeds a device token directly, bypassing UpsertAsync — for test setup.</summary>
    public DeviceToken Seed(DeviceToken token)
    {
        if (token.Id == Guid.Empty)
            token.Id = Guid.NewGuid();

        token.CreatedAt = DateTimeOffset.UtcNow;
        token.UpdatedAt = token.CreatedAt;
        _tokens[token.Id] = token;
        return token;
    }

    public Task<DeviceToken> UpsertAsync(
        Guid userId, string token, DevicePlatform platform, CancellationToken ct = default)
    {
        var existing = _tokens.Values.FirstOrDefault(x => x.Token == token);
        if (existing is not null)
        {
            existing.UserId = userId;
            existing.Platform = platform;
            existing.IsActive = true;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(existing);
        }

        var deviceToken = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            Platform = platform,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _tokens[deviceToken.Id] = deviceToken;
        return Task.FromResult(deviceToken);
    }

    public Task<DeviceToken?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_tokens.GetValueOrDefault(id));

    public Task DeactivateAsync(DeviceToken token, CancellationToken ct = default)
    {
        token.IsActive = false;
        _tokens[token.Id] = token;
        return Task.CompletedTask;
    }
}
