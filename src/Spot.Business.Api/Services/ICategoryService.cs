using Spot.Business.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public interface ICategoryService
{
    Task<PaginatedResponse<CategoryDto>> ListAsync(
        int page,
        int pageSize,
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        CancellationToken ct = default);

    /// <exception cref="CategoryNotFoundException">The given parent category doesn't exist.</exception>
    /// <exception cref="InvalidCategoryHierarchyException">The given parent is itself a subcategory.</exception>
    /// <exception cref="Repositories.DuplicateCategoryException">
    /// A category with the same (parentCategoryId, name) already exists.
    /// </exception>
    Task<CategoryDto> CreateAsync(CategoryCreateRequest request, CancellationToken ct = default);

    /// <summary>Null if no category with <paramref name="categoryId"/> exists.</summary>
    /// <exception cref="CategoryNotFoundException">The new parent category doesn't exist.</exception>
    /// <exception cref="InvalidCategoryHierarchyException">
    /// The update would make the category its own parent, use a subcategory as a parent, or turn
    /// a category that already has subcategories into a subcategory itself.
    /// </exception>
    /// <exception cref="Repositories.DuplicateCategoryException">
    /// A category with the same (parentCategoryId, name) already exists.
    /// </exception>
    Task<CategoryDto?> UpdateAsync(Guid categoryId, CategoryUpdateRequest request, CancellationToken ct = default);

    /// <returns>False if no category with <paramref name="categoryId"/> exists; true if deleted.</returns>
    /// <exception cref="Repositories.CategoryHasSubcategoriesException">
    /// The category has one or more subcategories. Deletion never cascades.
    /// </exception>
    Task<bool> DeleteAsync(Guid categoryId, CancellationToken ct = default);
}
