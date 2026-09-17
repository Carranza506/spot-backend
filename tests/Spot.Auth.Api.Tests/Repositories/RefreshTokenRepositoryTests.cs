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
/// filtering in <see cref="RefreshTokenRepository.FindActiveByHashAsync"/> — is a plain LINQ
/// predicate that InMemory evaluates faithfully. If a future change makes this entity depend on
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
    public async Task RevokeAsync_SetsRevokedAt_AndPersistsIt()
    {
        var userId = Guid.NewGuid();
        var token = await SeedTokenAsync(userId, "hash-1");
        var before = DateTimeOffset.UtcNow;

        await _repository.RevokeAsync(token);

        Assert.NotNull(token.RevokedAt);
        Assert.True(token.RevokedAt >= before);

        // Re-read through a fresh context against the same InMemory database name to prove the
        // change was actually saved, not just mutated on the in-memory object graph.
        await using var freshDb = new AuthDbContext(_options);
        var persisted = await freshDb.RefreshTokens.SingleAsync(x => x.Id == token.Id);
        Assert.NotNull(persisted.RevokedAt);
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
