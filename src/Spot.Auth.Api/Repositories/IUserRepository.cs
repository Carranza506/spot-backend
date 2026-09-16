using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.Repositories;

public interface IUserRepository
{
    /// <summary>
    /// Loads a user by id, including their linked auth providers, or null if no such user
    /// exists. The returned entity is tracked, so a caller can mutate it in place and persist
    /// the change with <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Persists changes made to a tracked <see cref="User"/> and refreshes its scalar
    /// properties from the database afterwards. The refresh matters specifically for
    /// <c>UpdatedAt</c>: it is set by a Postgres trigger (see the
    /// AddPgcryptoExtensionAndUsersUpdatedAtTrigger migration), not by application code, so
    /// without it the in-memory value would still be the pre-update timestamp.
    /// </summary>
    Task SaveChangesAsync(User user, CancellationToken ct = default);
}
