using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Tests.Fakes;

/// <summary>In-memory stand-in for IUserRepository — no database involved.</summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = [];

    public User? CreatedUser { get; private set; }
    public RefreshToken? CreatedRefreshToken { get; private set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task CreateAsync(User user, RefreshToken refreshToken, CancellationToken ct = default)
    {
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        // Stands in for what the database would normally generate.
        user.Id = Guid.NewGuid();
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = user.CreatedAt;

        CreatedUser = user;
        CreatedRefreshToken = refreshToken;
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    // Not exercised by AuthControllerTests (GET/PATCH /auth/me go through the separately-faked
    // IUserProfileService instead) — kept as a straightforward in-memory implementation so this
    // fake satisfies the full IUserRepository contract for any test that does need it directly.
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(_users.Values.FirstOrDefault(u => u.Email == email));

    public Task SaveChangesAsync(User user, CancellationToken ct = default)
    {
        user.UpdatedAt = DateTime.UtcNow;
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public RefreshToken? LastAddedRefreshToken { get; private set; }

    public Task AddRefreshTokenAsync(User user, RefreshToken refreshToken, CancellationToken ct = default)
    {
        LastAddedRefreshToken = refreshToken;
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    /// <summary>Seeds a user directly (e.g. for login tests), bypassing <see cref="CreateAsync"/>.</summary>
    public void Seed(User user) => _users[user.Id] = user;

    public void Reset()
    {
        CreatedUser = null;
        CreatedRefreshToken = null;
        LastAddedRefreshToken = null;
        ExceptionToThrow = null;
        _users.Clear();
    }
}
