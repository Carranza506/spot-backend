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

    private static bool IsUniqueNameViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_categories_parent_category_id_name",
        };
}
