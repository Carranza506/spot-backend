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

    /// <summary>The business's location (business_locations), or null if it hasn't set one yet. Not tracked.</summary>
    Task<BusinessLocation?> GetLocationAsync(Guid businessId, CancellationToken ct = default);

    /// <summary>
    /// Creates or replaces the single business_locations row of <c>location.BusinessId</c>
    /// (business_id is unique): inserts <paramref name="location"/> if the business has no row
    /// yet, otherwise copies its fields onto the existing row, keeping that row's id and
    /// created_at. Returns the stored row refreshed from the database (id, created_at and
    /// updated_at come from Postgres defaults/triggers, not application code).
    /// </summary>
    /// <remarks>
    /// Race-safe: if a concurrent first PUT inserts the row between the lookup and the insert
    /// (IX_business_locations_business_id), the insert is retried once as an update of that row.
    /// </remarks>
    Task<BusinessLocation> UpsertLocationAsync(BusinessLocation location, CancellationToken ct = default);
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

    /// <summary>
    /// The favorite businesses of <paramref name="userId"/> (favorite_businesses), with
    /// <see cref="FavoriteBusiness.Business"/> loaded. Favorites of an inactive business are
    /// filtered out before paging, so <c>Total</c> counts only active ones. Newest first, then by
    /// business id so pages are stable when two favorites share a created_at.
    /// </summary>
    Task<(IReadOnlyList<FavoriteBusiness> Items, int Total)> ListFavoritesAsync(
        Guid userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Saves <paramref name="businessId"/> as a favorite of <paramref name="userId"/>. Idempotent:
    /// an existing favorite is left untouched (its created_at is kept), including when a concurrent
    /// request inserts the same row first (duplicate key, SQLSTATE 23505). Callers are expected to
    /// have already checked the business exists and is active.
    /// </summary>
    Task AddFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default);

    /// <summary>Hard-deletes the caller's favorite row, if any. No-op when there is none.</summary>
    Task RemoveFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default);

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
