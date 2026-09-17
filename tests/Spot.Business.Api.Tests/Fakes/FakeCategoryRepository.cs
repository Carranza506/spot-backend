using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for ICategoryRepository — no database involved. Shared between
/// CategoryServiceTests (direct construction) and CategoriesApiFactory (DI-swapped), same as
/// Spot.Auth.Api.Tests' fakes. Simulates the real repository's two DB-enforced invariants
/// (duplicate name under the same parent, can't delete a category with subcategories) in-memory,
/// so callers see the same exceptions regardless of whether they went through the up-front
/// service-layer check or straight into the repository.
/// </summary>
public sealed class FakeCategoryRepository : ICategoryRepository
{
    private readonly Dictionary<Guid, Category> _categories = [];

    public void Reset() => _categories.Clear();

    /// <summary>Seeds a category directly, bypassing CreateAsync's own validation — for test setup.</summary>
    public Category Seed(Category category)
    {
        if (category.Id == Guid.Empty)
            category.Id = Guid.NewGuid();

        category.CreatedAt = DateTimeOffset.UtcNow;
        category.UpdatedAt = category.CreatedAt;
        _categories[category.Id] = category;
        return category;
    }

    public Task<(IReadOnlyList<Category> Items, int Total)> ListAsync(
        ParentCategoryFilter parentFilter, Guid? parentCategoryId, int page, int pageSize, CancellationToken ct = default)
    {
        IEnumerable<Category> query = _categories.Values;

        query = parentFilter switch
        {
            ParentCategoryFilter.RootOnly => query.Where(c => c.ParentCategoryId is null),
            ParentCategoryFilter.Specific => query.Where(c => c.ParentCategoryId == parentCategoryId),
            _ => query,
        };

        var all = query.OrderBy(c => c.Name).ToList();
        var page_ = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(((IReadOnlyList<Category>)page_, all.Count));
    }

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (!_categories.TryGetValue(id, out var category))
            return Task.FromResult<Category?>(null);

        // Mirrors the real repository's .Include(c => c.SubCategories).
        category.SubCategories = _categories.Values.Where(c => c.ParentCategoryId == id).ToList();
        return Task.FromResult<Category?>(category);
    }

    public Task<bool> ExistsByParentAndNameAsync(
        Guid? parentCategoryId, string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        var exists = _categories.Values.Any(c =>
            c.ParentCategoryId == parentCategoryId && c.Name == name && c.Id != excludeId);

        return Task.FromResult(exists);
    }

    public Task CreateAsync(Category category, CancellationToken ct = default)
    {
        if (_categories.Values.Any(c => c.ParentCategoryId == category.ParentCategoryId && c.Name == category.Name))
            throw new DuplicateCategoryException(category.ParentCategoryId, category.Name);

        category.Id = Guid.NewGuid();
        category.CreatedAt = DateTimeOffset.UtcNow;
        category.UpdatedAt = category.CreatedAt;
        _categories[category.Id] = category;

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(Category category, CancellationToken ct = default)
    {
        var collides = _categories.Values.Any(c =>
            c.Id != category.Id && c.ParentCategoryId == category.ParentCategoryId && c.Name == category.Name);
        if (collides)
            throw new DuplicateCategoryException(category.ParentCategoryId, category.Name);

        category.UpdatedAt = DateTimeOffset.UtcNow;
        _categories[category.Id] = category;

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!_categories.ContainsKey(id))
            return Task.FromResult(false);

        if (_categories.Values.Any(c => c.ParentCategoryId == id))
            throw new CategoryHasSubcategoriesException(id);

        _categories.Remove(id);
        return Task.FromResult(true);
    }
}
