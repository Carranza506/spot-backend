using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public sealed class CategoryRepository(BusinessDbContext db) : ICategoryRepository
{
    public async Task<(IReadOnlyList<Category> Items, int Total)> ListAsync(
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = db.Categories.AsNoTracking();

        query = parentFilter switch
        {
            ParentCategoryFilter.RootOnly => query.Where(c => c.ParentCategoryId == null),
            ParentCategoryFilter.Specific => query.Where(c => c.ParentCategoryId == parentCategoryId),
            _ => query,
        };

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Categories.Include(c => c.SubCategories).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<bool> ExistsByParentAndNameAsync(
        Guid? parentCategoryId, string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = db.Categories.AsNoTracking()
            .Where(c => c.ParentCategoryId == parentCategoryId && c.Name == name);

        if (excludeId.HasValue)
            query = query.Where(c => c.Id != excludeId.Value);

        return await query.AnyAsync(ct);
    }

    public async Task CreateAsync(Category category, CancellationToken ct = default)
    {
        db.Categories.Add(category);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueNameViolation(ex))
        {
            throw new DuplicateCategoryException(category.ParentCategoryId, category.Name);
        }
    }

    public async Task SaveChangesAsync(Category category, CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueNameViolation(ex))
        {
            throw new DuplicateCategoryException(category.ParentCategoryId, category.Name);
        }

        await db.Entry(category).ReloadAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var category = await GetByIdAsync(id, ct);
        if (category is null)
            return false;

        // Up-front check for a clean 409 in the common case — see the catch below for the
        // race-proof fallback (a subcategory created concurrently, after this check passed).
        if (category.SubCategories.Count > 0)
            throw new CategoryHasSubcategoriesException(id);

        db.Categories.Remove(category);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsSubcategoriesForeignKeyViolation(ex))
        {
            throw new CategoryHasSubcategoriesException(id);
        }

        return true;
    }

    private static bool IsUniqueNameViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_categories_parent_category_id_name",
        };

    private static bool IsSubcategoriesForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: "FK_categories_categories_parent_category_id",
        };
}
