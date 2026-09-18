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
    /// Loads the user linked to a given social login identity, including their linked auth
    /// providers, or null if no such link exists yet. The returned entity is tracked (see
    /// <see cref="GetByIdAsync"/>).
    /// </summary>
    Task<User?> GetByProviderAsync(AuthProvider provider, string providerUserId, CancellationToken ct = default);

    /// <summary>
    /// Loads a user by email, including their linked auth providers, or null if no such user
    /// exists. The returned entity is tracked (see <see cref="GetByIdAsync"/>).
    /// </summary>
    /// <remarks>Case-sensitive, same as the unique index on <c>users.email</c> — callers must
    /// normalize the email themselves (see AuthService.NormalizeEmail).</remarks>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Persists changes made to a tracked <see cref="User"/> and refreshes its scalar
    /// properties from the database afterwards. The refresh matters specifically for
    /// <c>UpdatedAt</c>: it is set by a Postgres trigger (see the
    /// AddPgcryptoExtensionAndUsersUpdatedAtTrigger migration), not by application code, so
    /// without it the in-memory value would still be the pre-update timestamp.
    /// </summary>
    Task SaveChangesAsync(User user, CancellationToken ct = default);
}
