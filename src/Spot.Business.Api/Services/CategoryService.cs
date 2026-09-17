using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public sealed class CategoryService(ICategoryRepository repository) : ICategoryService
{
    public async Task<PaginatedResponse<CategoryDto>> ListAsync(
        int page,
        int pageSize,
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        CancellationToken ct = default)
    {
        var (items, total) = await repository.ListAsync(parentFilter, parentCategoryId, page, pageSize, ct);

        var dtos = items.Select(CategoryDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var pagination = new PaginationMeta(page, pageSize, total, totalPages);

        return new PaginatedResponse<CategoryDto>(dtos, pagination);
    }

    public async Task<CategoryDto> CreateAsync(CategoryCreateRequest request, CancellationToken ct = default)
    {
        if (request.ParentCategoryId.HasValue)
            await ValidateParentExistsAndIsRootAsync(request.ParentCategoryId.Value, ct);

        var name = request.Name.Trim();

        if (await repository.ExistsByParentAndNameAsync(request.ParentCategoryId, name, ct: ct))
            throw new DuplicateCategoryException(request.ParentCategoryId, name);

        var category = new Category
        {
            ParentCategoryId = request.ParentCategoryId,
            Name = name,
            Description = NormalizeDescription(request.Description),
        };

        await repository.CreateAsync(category, ct);

        return CategoryDto.FromEntity(category);
    }

    public async Task<CategoryDto?> UpdateAsync(Guid categoryId, CategoryUpdateRequest request, CancellationToken ct = default)
    {
        var category = await repository.GetByIdAsync(categoryId, ct);
        if (category is null)
            return null;

        var newParentCategoryId = request.ParentCategoryId.IsSet ? request.ParentCategoryId.Value : category.ParentCategoryId;
        var parentChanged = newParentCategoryId != category.ParentCategoryId;

        if (parentChanged)
        {
            if (newParentCategoryId == category.Id)
                throw new InvalidCategoryHierarchyException("Una categoría no puede ser su propio padre.");

            if (newParentCategoryId.HasValue)
            {
                // This category already has children of its own — making it someone else's
                // subcategory would create a two-level chain (its children would become
                // grandchildren of the new parent), which the single-level hierarchy forbids.
                if (category.SubCategories.Count > 0)
                {
                    throw new InvalidCategoryHierarchyException(
                        "Una categoría con subcategorías no puede convertirse en subcategoría.");
                }

                await ValidateParentExistsAndIsRootAsync(newParentCategoryId.Value, ct);
            }
        }

        var newName = request.Name is not null ? request.Name.Trim() : category.Name;

        if (parentChanged || newName != category.Name)
        {
            if (await repository.ExistsByParentAndNameAsync(newParentCategoryId, newName, excludeId: category.Id, ct: ct))
                throw new DuplicateCategoryException(newParentCategoryId, newName);
        }

        category.ParentCategoryId = newParentCategoryId;
        category.Name = newName;

        if (request.Description.IsSet)
            category.Description = NormalizeDescription(request.Description.Value);

        if (request.IsActive.HasValue)
            category.IsActive = request.IsActive.Value;

        await repository.SaveChangesAsync(category, ct);

        return CategoryDto.FromEntity(category);
    }

    /// <summary>Shared by Create and Update: a parent must exist and must itself be a root category.</summary>
    private async Task ValidateParentExistsAndIsRootAsync(Guid parentCategoryId, CancellationToken ct)
    {
        var parent = await repository.GetByIdAsync(parentCategoryId, ct);
        if (parent is null)
            throw new CategoryNotFoundException(parentCategoryId);

        if (parent.ParentCategoryId is not null)
            throw new InvalidCategoryHierarchyException("Una subcategoría no puede usarse como categoría padre.");
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
