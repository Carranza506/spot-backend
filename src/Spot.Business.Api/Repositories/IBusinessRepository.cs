using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Repositories;

public interface IBusinessRepository
{
    /// <summary>
    /// Loads a business by id, active or not, or null if none exists. The returned entity is
    /// tracked, so a caller can mutate it in place and persist with <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<BusinessEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Same as <see cref="GetByIdAsync"/>, but looked up by the owning account (businesses.account_id).</summary>
    Task<BusinessEntity?> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    Task<bool> ExistsByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    /// <exception cref="BusinessAlreadyExistsException">
    /// The account already has a business — re-checked against IX_businesses_account_id to close
    /// the race window between two concurrent creates. Callers are expected to have already
    /// checked <see cref="ExistsByAccountIdAsync"/> up front; this is only the safety net.
    /// </exception>
    /// <exception cref="DuplicateBusinessSlugException">
    /// The slug was taken concurrently, after <see cref="SlugExistsAsync"/> said it was free
    /// (IX_businesses_slug). The entity stays tracked as Added, so the caller can change its slug
    /// and call this again.
    /// </exception>
    Task CreateAsync(BusinessEntity business, CancellationToken ct = default);

    /// <summary>
    /// Persists changes made to a tracked business (from <see cref="GetByIdAsync"/> or
    /// <see cref="GetByAccountIdAsync"/>) and refreshes it from the database afterwards
    /// (<c>updated_at</c> is set by a Postgres trigger, not application code).
    /// </summary>
    Task SaveChangesAsync(BusinessEntity business, CancellationToken ct = default);
}
