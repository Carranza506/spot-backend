using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Spot.Auth.Api.Configuration;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Services;

/// <summary>
/// Exercises <see cref="RefreshTokenService"/> wired to its real collaborators (the actual
/// SHA-256 hasher and a repository backed by a real <see cref="AuthDbContext"/>), not mocks —
/// this is what proves hashing on write and hashing on lookup actually agree with each other,
/// which a test that stubs either collaborator could not. <see cref="ITokenService"/> is faked
/// since access-token issuing itself is already covered by JwtTokenServiceTests.
/// </summary>
public sealed class RefreshTokenServiceTests : IDisposable
{
    private readonly DbContextOptions<AuthDbContext> _options;
    private readonly AuthDbContext _db;
    private readonly FakeTokenService _tokenService = new();
    private readonly RefreshTokenService _service;

    public RefreshTokenServiceTests()
    {
        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AuthDbContext(_options);
        _service = CreateService(_db);
    }

    public void Dispose() => _db.Dispose();

    private RefreshTokenService CreateService(AuthDbContext db) => new(
        new RefreshTokenRepository(db),
        new Sha256RefreshTokenHasher(),
        new UserRepository(db),
        _tokenService,
        new RefreshTokenIssuer(Options.Create(new RefreshTokenOptions { ExpirationDays = 30 }), new Sha256RefreshTokenHasher()));

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
    public async Task RefreshAsync_ValidActiveToken_RevokesIt_AndReturnsANewPair()
    {
        const string rawToken = "a-real-high-entropy-refresh-token";
        var user = await SeedUserAsync();
        var stored = await SeedTokenAsync(user.Id, rawToken);

        var result = await _service.RefreshAsync(rawToken);

        Assert.NotNull(result);
        Assert.Equal("Bearer", result!.TokenType);
        Assert.NotEqual(rawToken, result.RefreshToken);
        Assert.Equal(_tokenService.IssuedFor, (user.Id.ToString(), user.Role.ToString()));

        var reloadedOld = await _db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == stored.Id);
        Assert.NotNull(reloadedOld.RevokedAt);

        var newStored = await _db.RefreshTokens.AsNoTracking()
            .SingleAsync(x => x.UserId == user.Id && x.Id != stored.Id);
        Assert.Equal(new Sha256RefreshTokenHasher().Hash(result.RefreshToken), newStored.TokenHash);
    }

    [Fact]
    public async Task RefreshAsync_RotatedTokenCannotBeReplayed()
    {
        const string rawToken = "a-real-high-entropy-refresh-token";
        var user = await SeedUserAsync();
        await SeedTokenAsync(user.Id, rawToken);

        var first = await _service.RefreshAsync(rawToken);
        var replay = await _service.RefreshAsync(rawToken);

        Assert.NotNull(first);
        Assert.Null(replay);
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentReplayAttempts_OnlyOneSucceeds()
    {
        // Unlike the sequential test above (which only proves a call made AFTER a prior one
        // fully completed gets rejected — never in doubt), this drives two genuinely overlapping
        // calls for the SAME refresh token, each through its own AuthDbContext/service instance
        // against the same store, the way two concurrent HTTP requests actually would. Before the
        // atomic conditional revoke, both could read the token as active and both walk away with
        // a valid new token pair.
        const string rawToken = "a-real-high-entropy-refresh-token";
        var user = await SeedUserAsync();
        await SeedTokenAsync(user.Id, rawToken);

        await using var dbB = new AuthDbContext(_options);
        var serviceB = CreateService(dbB);

        var results = await Task.WhenAll(
            _service.RefreshAsync(rawToken),
            serviceB.RefreshAsync(rawToken));

        Assert.Single(results, r => r is not null);
        Assert.Single(results, r => r is null);
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ReturnsNull()
    {
        var result = await _service.RefreshAsync("never-issued-token");

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_AlreadyRevokedToken_ReturnsNull()
    {
        const string rawToken = "already-used-token";
        var user = await SeedUserAsync();
        await SeedTokenAsync(user.Id, rawToken, revokedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _service.RefreshAsync(rawToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNull()
    {
        const string rawToken = "expired-token";
        var user = await SeedUserAsync();
        await SeedTokenAsync(user.Id, rawToken, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _service.RefreshAsync(rawToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_TokenNamesAUserThatNoLongerExists_ReturnsNull_AndDoesNotRevoke()
    {
        const string rawToken = "orphaned-token";
        var stored = await SeedTokenAsync(Guid.NewGuid(), rawToken);

        var result = await _service.RefreshAsync(rawToken);

        Assert.Null(result);
        var reloaded = await _db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == stored.Id);
        Assert.Null(reloaded.RevokedAt);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@spot.cr",
            FirstName = "Test",
            LastName = "User",
            Role = UserRole.CLIENT,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return user;
    }

    private async Task<RefreshToken> SeedTokenAsync(
        Guid userId, string rawToken, DateTimeOffset? revokedAt = null, DateTimeOffset? expiresAt = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = new Sha256RefreshTokenHasher().Hash(rawToken),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = revokedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();

        return token;
    }

    /// <summary>Records how it was called instead of signing a real JWT.</summary>
    private sealed class FakeTokenService : ITokenService
    {
        public (string UserId, string Role)? IssuedFor { get; private set; }

        public AccessToken IssueAccessToken(string userId, string role)
        {
            IssuedFor = (userId, role);
            return new AccessToken("fake-access-token", 3600);
        }
    }
}
