using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Tests.Repositories;

/// <summary>
/// Integration tests against a real <see cref="AuthDbContext"/>/<c>SaveChangesAsync</c>
/// round-trip, using EF Core's InMemory provider — see RefreshTokenRepositoryTests.cs for why
/// that's an equivalent, Postgres-independent choice for this entity.
/// </summary>
public sealed class UserRepositoryTests : IDisposable
{
    private readonly DbContextOptions<AuthDbContext> _options;
    private readonly AuthDbContext _db;
    private readonly UserRepository _repository;

    public UserRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AuthDbContext(_options);
        _repository = new UserRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetByIdAsync_ExistingUser_ReturnsItWithLinkedProviders()
    {
        var user = await SeedUserAsync();
        _db.UserAuthProviders.Add(new UserAuthProvider
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = AuthProvider.GOOGLE,
            ProviderUserId = "google-123",
        });
        await _db.SaveChangesAsync();

        var found = await _repository.GetByIdAsync(user.Id);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found!.Id);
        Assert.Single(found.AuthProviders);
        Assert.Equal(AuthProvider.GOOGLE, found.AuthProviders.Single().Provider);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var found = await _repository.GetByIdAsync(Guid.NewGuid());

        Assert.Null(found);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsMutationsMadeOnTheTrackedEntity()
    {
        var user = await SeedUserAsync();
        var tracked = await _repository.GetByIdAsync(user.Id);
        tracked!.FirstName = "Updated";

        await _repository.SaveChangesAsync(tracked);

        await using var freshDb = new AuthDbContext(_options);
        var persisted = await freshDb.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("Updated", persisted.FirstName);
    }

    [Fact]
    public async Task GetByEmailAsync_ExistingUser_ReturnsItWithLinkedProviders()
    {
        var user = await SeedUserAsync();
        _db.UserAuthProviders.Add(new UserAuthProvider
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = AuthProvider.GOOGLE,
            ProviderUserId = "google-123",
        });
        await _db.SaveChangesAsync();

        var found = await _repository.GetByEmailAsync(user.Email);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found!.Id);
        Assert.Single(found.AuthProviders);
    }

    [Fact]
    public async Task GetByEmailAsync_UnknownEmail_ReturnsNull()
    {
        var found = await _repository.GetByEmailAsync("nobody@example.com");

        Assert.Null(found);
    }

    [Fact]
    public async Task AddRefreshTokenAsync_PersistsANewRefreshTokenForAnExistingUser()
    {
        var user = await SeedUserAsync();
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            TokenHash = "some-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };

        await _repository.AddRefreshTokenAsync(user, refreshToken);

        await using var freshDb = new AuthDbContext(_options);
        var persisted = await freshDb.RefreshTokens.SingleAsync(t => t.UserId == user.Id);
        Assert.Equal("some-hash", persisted.TokenHash);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "maria@example.com",
            FirstName = "María",
            LastName = "Rodríguez",
            Role = UserRole.CLIENT,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return user;
    }
}
