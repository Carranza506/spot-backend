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

    /// <summary>
    /// Loads a user by id, including their linked auth providers, or null if no such user
    /// exists. The returned entity is tracked, so a caller can mutate it in place and persist
    /// the change with <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Loads a user by email (expected already normalized — trimmed/lower-cased — by the
    /// caller), including their linked auth providers, or null if no such user exists. Used by
    /// login: the caller must treat "no such user" and "wrong password" identically, so it
    /// never reveals which one happened.
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Persists changes made to a tracked <see cref="User"/> and refreshes its scalar
    /// properties from the database afterwards. The refresh matters specifically for
    /// <c>UpdatedAt</c>: it is set by a Postgres trigger (see the
    /// AddPgcryptoExtensionAndUsersUpdatedAtTrigger migration), not by application code, so
    /// without it the in-memory value would still be the pre-update timestamp.
    /// </summary>
    Task SaveChangesAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Attaches a new refresh token to an already-persisted <paramref name="user"/> (a new login
    /// session) and saves it. Unlike <see cref="CreateAsync"/>, the user's id is already known —
    /// there's no foreign-key fix-up dance and no email-uniqueness race to guard against.
    /// </summary>
    Task AddRefreshTokenAsync(User user, RefreshToken refreshToken, CancellationToken ct = default);
}
