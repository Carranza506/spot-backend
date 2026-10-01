using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Repositories;

public sealed class BusinessRepository(BusinessDbContext db) : IBusinessRepository
{
    public Task<BusinessEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Businesses.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<BusinessEntity?> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default) =>
        db.Businesses.FirstOrDefaultAsync(b => b.AccountId == accountId, ct);

    public Task<bool> ExistsByAccountIdAsync(Guid accountId, CancellationToken ct = default) =>
        db.Businesses.AsNoTracking().AnyAsync(b => b.AccountId == accountId, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        db.Businesses.AsNoTracking().AnyAsync(b => b.Slug == slug, ct);

    public async Task CreateAsync(BusinessEntity business, CancellationToken ct = default)
    {
        if (db.Entry(business).State == EntityState.Detached)
            db.Businesses.Add(business);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_businesses_account_id"))
        {
            throw new BusinessAlreadyExistsException(business.AccountId);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_businesses_slug"))
        {
            throw new DuplicateBusinessSlugException(business.Slug);
        }
    }

    public async Task SaveChangesAsync(BusinessEntity business, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await db.Entry(business).ReloadAsync(ct);
    }

    public async Task<(IReadOnlyList<Category> Items, int Total)> ListCategoriesAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.BusinessCategories.AsNoTracking()
            .Where(bc => bc.BusinessId == businessId)
            .Select(bc => bc.Category);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<Category>> ReplaceCategoriesAsync(
        Guid businessId, IReadOnlyCollection<Guid> categoryIds, CancellationToken ct = default)
    {
        var existing = await db.BusinessCategories.Where(bc => bc.BusinessId == businessId).ToListAsync(ct);
        db.BusinessCategories.RemoveRange(existing);
        db.BusinessCategories.AddRange(categoryIds.Select(categoryId => new BusinessCategory
        {
            BusinessId = businessId,
            CategoryId = categoryId,
        }));

        await db.SaveChangesAsync(ct);

        return await db.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraintName;
}
