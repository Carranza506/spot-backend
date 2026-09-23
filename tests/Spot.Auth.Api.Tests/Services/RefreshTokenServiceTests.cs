using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Services;

/// <summary>
/// Exercises <see cref="RefreshTokenService"/> wired to its real collaborators (the actual
/// SHA-256 hasher and a repository backed by a real <see cref="AuthDbContext"/>), not mocks —
/// this is what proves hashing on write and hashing on lookup actually agree with each other,
/// which a test that stubs either collaborator could not.
/// </summary>
public sealed class RefreshTokenServiceTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly RefreshTokenService _service;

    public RefreshTokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AuthDbContext(options);
        _service = new RefreshTokenService(new RefreshTokenRepository(_db), new Sha256RefreshTokenHasher());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RevokeAsync_ValidOwnedToken_RevokesIt()
    {
        const string rawToken = "a-real-high-entropy-refresh-token";
        var userId = Guid.NewGuid();
        var stored = await SeedTokenAsync(userId, rawToken);

        await _service.RevokeAsync(userId, rawToken);

        var reloaded = await _db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == stored.Id);
        Assert.NotNull(reloaded.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_TokenOwnedByAnotherUser_DoesNotRevokeIt()
    {
        const string rawToken = "someone-elses-refresh-token";
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var stored = await SeedTokenAsync(owner, rawToken);

        // Simulates an attacker with their own valid access token (attacker's id) trying to log
        // out using a refresh token that isn't theirs.
        await _service.RevokeAsync(attacker, rawToken);

        var reloaded = await _db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == stored.Id);
        Assert.Null(reloaded.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_DoesNotThrow()
    {
        // No token was ever seeded — this must behave like a no-op logout, not an error, so the
        // endpoint can always answer 204 regardless of whether the token existed.
        var exception = await Record.ExceptionAsync(() =>
            _service.RevokeAsync(Guid.NewGuid(), "never-issued-token"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevokedToken_StaysRevoked_DoesNotThrow()
    {
        const string rawToken = "already-logged-out-token";
        var userId = Guid.NewGuid();
        var stored = await SeedTokenAsync(userId, rawToken, revokedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var originalRevokedAt = stored.RevokedAt;

        await _service.RevokeAsync(userId, rawToken);

        var reloaded = await _db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == stored.Id);
        // Unchanged: FindActiveByHashAsync excludes already-revoked tokens, so this call is a
        // no-op rather than overwriting the original revocation time.
        Assert.Equal(originalRevokedAt, reloaded.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllActiveForUserAsync_RevokesTheUsersActiveTokens_ButNotAnotherUsers()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = await SeedTokenAsync(userId, "my-token");
        var someoneElses = await SeedTokenAsync(other, "their-token");

        await _service.RevokeAllActiveForUserAsync(userId);

        var reloadedMine = await _db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == mine.Id);
        var reloadedTheirs = await _db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == someoneElses.Id);
        Assert.NotNull(reloadedMine.RevokedAt);
        Assert.Null(reloadedTheirs.RevokedAt);
    }

    private async Task<RefreshToken> SeedTokenAsync(Guid userId, string rawToken, DateTimeOffset? revokedAt = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = new Sha256RefreshTokenHasher().Hash(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = revokedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();

        return token;
    }
}
