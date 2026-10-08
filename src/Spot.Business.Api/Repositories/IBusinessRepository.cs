using Spot.Business.Api.Models;
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

    /// <summary>Categories assigned to a business (business_categories), ordered by name and paged.</summary>
    Task<(IReadOnlyList<Category> Items, int Total)> ListCategoriesAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Replaces the full business_categories set for <paramref name="businessId"/> with
    /// <paramref name="categoryIds"/> and returns the resulting categories. Callers are expected
    /// to have already validated every id exists (see <see cref="ICategoryRepository.ExistingIdsAsync"/>).
    /// </summary>
    Task<IReadOnlyList<Category>> ReplaceCategoriesAsync(
        Guid businessId, IReadOnlyCollection<Guid> categoryIds, CancellationToken ct = default);

    /// <summary>The weekly schedule of a business (business_hours), ordered by day of week (0 = Sunday) and paged.</summary>
    Task<(IReadOnlyList<BusinessHours> Items, int Total)> ListHoursAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Replaces the full weekly schedule of <paramref name="businessId"/> with <paramref name="schedule"/>
    /// in one transaction: days already stored are updated in place, new days are inserted and days
    /// missing from <paramref name="schedule"/> are deleted. Returns the resulting schedule ordered by
    /// day. Callers are expected to have already validated it (one entry per day, valid times).
    /// </summary>
    Task<IReadOnlyList<BusinessHours>> ReplaceHoursAsync(
        Guid businessId, IReadOnlyCollection<BusinessHours> schedule, CancellationToken ct = default);
}
