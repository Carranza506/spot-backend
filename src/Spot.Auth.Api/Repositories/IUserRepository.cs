using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.Repositories;

public interface IUserRepository
{
    /// <summary>
    /// Persists a new user together with its first refresh token in a single transaction.
    /// </summary>
    /// <exception cref="DuplicateEmailException">
    /// <paramref name="user"/>'s email is already registered — checked up front, and re-checked
    /// against the database's unique constraint to close the race window between two concurrent
    /// registrations for the same email.
    /// </exception>
    Task CreateAsync(User user, RefreshToken refreshToken, CancellationToken ct = default);
}
