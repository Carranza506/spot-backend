using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public interface ICategoryRepository
{
    /// <summary>
    /// Lists categories, ordered by name, filtered per <paramref name="parentFilter"/> and paged.
    /// </summary>
    Task<(IReadOnlyList<Category> Items, int Total)> ListAsync(
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Loads a category by id, including its direct subcategories, or null if none exists. The
    /// returned entity is tracked, so a caller can mutate it in place and persist the change with
    /// <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Whether a category named <paramref name="name"/> already exists under
    /// <paramref name="parentCategoryId"/>. Pass <paramref name="excludeId"/> when checking this
    /// for an update, so the category being updated doesn't collide with itself.
    /// </summary>
    Task<bool> ExistsByParentAndNameAsync(
        Guid? parentCategoryId, string name, Guid? excludeId = null, CancellationToken ct = default);

    /// <exception cref="DuplicateCategoryException">
    /// A category with the same (ParentCategoryId, Name) already exists — re-checked against the
    /// database's unique index to close the race window between two concurrent creates. Callers
    /// are expected to have already checked <see cref="ExistsByParentAndNameAsync"/> up front;
    /// this is only the race-proof safety net, same pattern as Spot.Auth.Api's UserRepository.
    /// </exception>
    Task CreateAsync(Category category, CancellationToken ct = default);

    /// <summary>
    /// Persists changes made to a tracked <see cref="Category"/> (from <see cref="GetByIdAsync"/>)
    /// and refreshes its scalar properties from the database afterwards (<c>UpdatedAt</c> is set
    /// by a Postgres trigger, not application code).
    /// </summary>
    /// <exception cref="DuplicateCategoryException">Same race-proof safety net as <see cref="CreateAsync"/>.</exception>
    Task SaveChangesAsync(Category category, CancellationToken ct = default);

    /// <returns>False if no category with <paramref name="id"/> exists; true if it was deleted.</returns>
    /// <exception cref="CategoryHasSubcategoriesException">
    /// The category has one or more subcategories — re-checked against the database's
    /// <c>ON DELETE RESTRICT</c> foreign key to close the race window between the check and the
    /// delete (e.g. a subcategory created concurrently, after this method's own check passed).
    /// Deletion never cascades to subcategories.
    /// </exception>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
