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

    public Task<User?> GetByProviderAsync(AuthProvider provider, string providerUserId, CancellationToken ct = default) =>
        Task.FromResult(_users.Values.FirstOrDefault(
            u => u.AuthProviders.Any(p => p.Provider == provider && p.ProviderUserId == providerUserId)));

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(_users.Values.FirstOrDefault(u => u.Email == email));

    public Task SaveChangesAsync(User user, CancellationToken ct = default)
    {
        user.UpdatedAt = DateTime.UtcNow;
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public void Reset()
    {
        CreatedUser = null;
        CreatedRefreshToken = null;
        ExceptionToThrow = null;
        _users.Clear();
    }

    /// <summary>Pre-populates an existing user, bypassing CreateAsync — for tests that need a
    /// user to already exist (e.g. a returning Google user, or an email/password account being
    /// linked to Google for the first time).</summary>
    public void Seed(User user)
    {
        if (user.Id == Guid.Empty)
            user.Id = Guid.NewGuid();

        _users[user.Id] = user;
    }
}
