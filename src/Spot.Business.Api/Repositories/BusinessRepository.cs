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

    public async Task<(IReadOnlyList<BusinessEntity> Items, int Total)> SearchAsync(
        string? q, Guid? categoryId, string? city, string? province,
        int page, int pageSize, CancellationToken ct = default)
    {
        // Only active businesses are publicly searchable — same visibility rule as GetPublicAsync.
        var query = db.Businesses.AsNoTracking().Where(b => b.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            // ILIKE is case-insensitive (decision: no accent handling). The term is a literal, so
            // escape LIKE's wildcards (% _) and the escape char itself, then wrap in %...%.
            var pattern = $"%{EscapeLikePattern(q.Trim())}%";
            query = query.Where(b =>
                EF.Functions.ILike(b.Name, pattern, "\\")
                || (b.Description != null && EF.Functions.ILike(b.Description, pattern, "\\")));
        }

        if (categoryId is { } catId)
            query = query.Where(b => db.BusinessCategories.Any(bc => bc.BusinessId == b.Id && bc.CategoryId == catId));

        // city/province live on business_locations. The join only narrows when a value is given,
        // so a business without a location still shows up unless one of these is filtered on.
        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(b => b.Location != null && b.Location.City == city.Trim());

        if (!string.IsNullOrWhiteSpace(province))
            query = query.Where(b => b.Location != null && b.Location.Province == province.Trim());

        var total = await query.CountAsync(ct);

        // Compute the offset as long so a huge page can't overflow int into a negative OFFSET
        // (Postgres rejects that with a 500). Past the last row there's nothing to return, and
        // guarding here also keeps the Skip cast safe: a valid offset is < total, which is an int.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
            return ([], total);

        var items = await query
            .OrderBy(b => b.Name)
            .ThenBy(b => b.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <summary>Escapes LIKE/ILIKE wildcards so a user's search term is matched literally (escape char: backslash).</summary>
    public static string EscapeLikePattern(string input) =>
        input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

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
