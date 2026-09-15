using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Tests.Fakes;

/// <summary>In-memory stand-in for IUserRepository — no database involved.</summary>
public sealed class FakeUserRepository : IUserRepository
{
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
        return Task.CompletedTask;
    }

    public void Reset()
    {
        CreatedUser = null;
        CreatedRefreshToken = null;
        ExceptionToThrow = null;
    }
}
