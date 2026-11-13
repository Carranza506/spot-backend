using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Tests.Repositories;

/// <summary>
/// Integration tests against a real <see cref="AuthDbContext"/>/<c>SaveChangesAsync</c>
/// round-trip, using EF Core's InMemory provider instead of a real Postgres (e.g. via
/// Testcontainers).
/// </summary>
/// <remarks>
/// This is a deliberate, scoped choice, not an oversight: every column <see cref="RefreshToken"/>
/// maps to (uuid, text, timestamptz) is provider-agnostic — nothing here depends on a Postgres
/// native enum, <c>jsonb</c>, PostGIS, or a trigger/exclusion-constraint the way e.g. Business.Api
/// or Booking.Api's entities do. What's actually under test — the ownership/active-token
/// filtering in <see cref="RefreshTokenRepository.FindActiveByHashAsync"/>, and (via
/// <see cref="RevokeAsync_ConcurrentRevokes_OnlyOneSucceeds"/>) the optimistic-concurrency check
/// EF's own SaveChanges pipeline performs for a property marked <c>IsConcurrencyToken()</c> — is
/// provider-agnostic behavior that InMemory implements faithfully in its own change-tracker layer,
/// not something that needs real SQL translation. If a future change makes this entity depend on
/// Postgres-specific behavior, switch this suite to Testcontainers.PostgreSql instead.
/// </remarks>
public sealed class RefreshTokenRepositoryTests : IDisposable
{
    private readonly DbContextOptions<AuthDbContext> _options;
    private readonly AuthDbContext _db;
    private readonly RefreshTokenRepository _repository;

    public RefreshTokenRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<AuthDbContext>()
            // Unique name per test instance so tests don't share state (xUnit creates a new
            // class instance per test, so this constructor runs fresh every time).
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AuthDbContext(_options);
        _repository = new RefreshTokenRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FindActiveByHashAsync_MatchingUserAndHash_ReturnsToken()
    {
        var userId = Guid.NewGuid();
        var token = await SeedTokenAsync(userId, "hash-1");

        var found = await _repository.FindActiveByHashAsync(userId, "hash-1");

        Assert.NotNull(found);
        Assert.Equal(token.Id, found!.Id);
    }

    [Fact]
    public async Task FindActiveByHashAsync_HashBelongsToAnotherUser_ReturnsNull()
    {
        // The core security property this repository exists to enforce: a user id from a
        // caller's own (valid) access token must never match another user's refresh token,
        // even if the raw token value/hash is somehow known.
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        await SeedTokenAsync(owner, "victims-hash");

        var found = await _repository.FindActiveByHashAsync(attacker, "victims-hash");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindActiveByHashAsync_AlreadyRevoked_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await SeedTokenAsync(userId, "hash-1", revokedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var found = await _repository.FindActiveByHashAsync(userId, "hash-1");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindActiveByHashAsync_Expired_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await SeedTokenAsync(userId, "hash-1", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var found = await _repository.FindActiveByHashAsync(userId, "hash-1");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindActiveByHashAsync_UnknownHash_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await SeedTokenAsync(userId, "hash-1");

        var found = await _repository.FindActiveByHashAsync(userId, "some-other-hash");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindActiveByHashAsync_HashOnly_MatchingHash_ReturnsTokenRegardlessOfOwner()
    {
        // Unlike the userId-scoped overload, this one is used by /auth/refresh, which has no
        // access token to read an owner from yet — the hash alone must be enough to find it.
        var userId = Guid.NewGuid();
        var token = await SeedTokenAsync(userId, "hash-1");

        var found = await _repository.FindActiveByHashAsync("hash-1");

        Assert.NotNull(found);
        Assert.Equal(token.Id, found!.Id);
    }

    [Fact]
    public async Task FindActiveByHashAsync_HashOnly_AlreadyRevoked_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await SeedTokenAsync(userId, "hash-1", revokedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var found = await _repository.FindActiveByHashAsync("hash-1");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindActiveByHashAsync_HashOnly_Expired_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await SeedTokenAsync(userId, "hash-1", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var found = await _repository.FindActiveByHashAsync("hash-1");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindActiveByHashAsync_HashOnly_UnknownHash_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await SeedTokenAsync(userId, "hash-1");

        var found = await _repository.FindActiveByHashAsync("some-other-hash");

        Assert.Null(found);
    }

    [Fact]
    public async Task CreateAsync_PersistsTheNewToken()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = "new-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.CreateAsync(token);

        await using var freshDb = new AuthDbContext(_options);
        var persisted = await freshDb.RefreshTokens.SingleAsync(x => x.Id == token.Id);
        Assert.Equal("new-hash", persisted.TokenHash);
    }

    [Fact]
    public async Task RevokeAsync_ActiveToken_ReturnsTrue_AndPersistsRevokedAt()
    {
        var userId = Guid.NewGuid();
        var token = await SeedTokenAsync(userId, "hash-1");
        var before = DateTimeOffset.UtcNow;

        var revoked = await _repository.RevokeAsync(token);

        Assert.True(revoked);
        Assert.NotNull(token.RevokedAt);
        Assert.True(token.RevokedAt >= before);

        // Re-read through a fresh context against the same InMemory database name to prove the
        // change was actually saved, not just mutated on the in-memory object graph.
        await using var freshDb = new AuthDbContext(_options);
        var persisted = await freshDb.RefreshTokens.SingleAsync(x => x.Id == token.Id);
        Assert.NotNull(persisted.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_ConcurrentRevokes_OnlyOneSucceeds()
    {
        // The core of the concurrency fix: two overlapping calls revoking the SAME active token
        // (as two genuinely simultaneous /auth/refresh requests replaying the same refresh token
        // would) must not both succeed — that would defeat single-use rotation. Each side gets its
        // own DbContext/tracked copy of the token, matching what two concurrent HTTP requests
        // would actually do (each request scope resolves its own AuthDbContext), and RevokedAt
        // being a concurrency token (see AuthDbContext) is what makes only one of the two
        // SaveChangesAsync calls actually able to apply its update.
        var userId = Guid.NewGuid();
        await SeedTokenAsync(userId, "hash-1");

        await using var dbA = new AuthDbContext(_options);
        await using var dbB = new AuthDbContext(_options);
        var repositoryA = new RefreshTokenRepository(dbA);
        var repositoryB = new RefreshTokenRepository(dbB);

        var tokenA = await repositoryA.FindActiveByHashAsync(userId, "hash-1");
        var tokenB = await repositoryB.FindActiveByHashAsync(userId, "hash-1");

        var results = await Task.WhenAll(
            repositoryA.RevokeAsync(tokenA!),
            repositoryB.RevokeAsync(tokenB!));

        Assert.Single(results, true);
        Assert.Single(results, false);

        await using var freshDb = new AuthDbContext(_options);
        var persisted = await freshDb.RefreshTokens.SingleAsync(x => x.UserId == userId);
        Assert.NotNull(persisted.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllActiveForUserAsync_RevokesOnlyThatUsersActiveTokens()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var active1 = await SeedTokenAsync(userId, "hash-1");
        var active2 = await SeedTokenAsync(userId, "hash-2");
        var alreadyRevoked = await SeedTokenAsync(userId, "hash-3", revokedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var expired = await SeedTokenAsync(userId, "hash-4", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var othersToken = await SeedTokenAsync(other, "hash-5");

        await _repository.RevokeAllActiveForUserAsync(userId);

        await using var freshDb = new AuthDbContext(_options);
        Assert.NotNull((await freshDb.RefreshTokens.SingleAsync(x => x.Id == active1.Id)).RevokedAt);
        Assert.NotNull((await freshDb.RefreshTokens.SingleAsync(x => x.Id == active2.Id)).RevokedAt);
        // Untouched: already revoked, already expired, and belonging to someone else.
        Assert.Equal(alreadyRevoked.RevokedAt, (await freshDb.RefreshTokens.SingleAsync(x => x.Id == alreadyRevoked.Id)).RevokedAt);
        Assert.Null((await freshDb.RefreshTokens.SingleAsync(x => x.Id == expired.Id)).RevokedAt);
        Assert.Null((await freshDb.RefreshTokens.SingleAsync(x => x.Id == othersToken.Id)).RevokedAt);
    }

    [Fact]
    public async Task RevokeAllActiveForUserAsync_NoActiveTokens_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => _repository.RevokeAllActiveForUserAsync(Guid.NewGuid()));

        Assert.Null(exception);
    }

    private async Task<RefreshToken> SeedTokenAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = revokedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();

        return token;
    }
}
