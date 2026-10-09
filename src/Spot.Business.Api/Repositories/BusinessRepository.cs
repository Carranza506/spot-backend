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

    public async Task<(IReadOnlyList<FavoriteBusiness> Items, int Total)> ListFavoritesAsync(
        Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.FavoriteBusinesses.AsNoTracking()
            .Include(f => f.Business)
            .Where(f => f.UserId == userId && f.Business.IsActive);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.BusinessId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task AddFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        if (await db.FavoriteBusinesses.AnyAsync(f => f.UserId == userId && f.BusinessId == businessId, ct))
            return;

        var favorite = new FavoriteBusiness { UserId = userId, BusinessId = businessId };
        db.FavoriteBusinesses.Add(favorite);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent PUT inserted the same (user_id, business_id) first: it's already a
            // favorite, which is the outcome the caller asked for. Matched by SQLSTATE only — the
            // PK is PK_favorite_businesses in the migration but favorite_businesses_pkey in db/Spot.sql.
            db.Entry(favorite).State = EntityState.Detached;
        }
    }

    public async Task RemoveFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        var favorite = await db.FavoriteBusinesses
            .FirstOrDefaultAsync(f => f.UserId == userId && f.BusinessId == businessId, ct);
        if (favorite is null)
            return;

        db.FavoriteBusinesses.Remove(favorite);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<BusinessHours> Items, int Total)> ListHoursAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.BusinessHours.AsNoTracking().Where(h => h.BusinessId == businessId);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(h => h.DayOfWeek)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<BusinessHours>> ReplaceHoursAsync(
        Guid businessId, IReadOnlyCollection<BusinessHours> schedule, CancellationToken ct = default)
    {
        var existing = await db.BusinessHours.Where(h => h.BusinessId == businessId).ToListAsync(ct);

        // Updating a stored day in place (instead of delete + insert) keeps its id and created_at,
        // and never has two rows for the same day at once on the (business_id, day_of_week) index.
        foreach (var day in schedule)
        {
            var current = existing.FirstOrDefault(h => h.DayOfWeek == day.DayOfWeek);
            if (current is null)
            {
                day.BusinessId = businessId;
                db.BusinessHours.Add(day);
                continue;
            }

            current.OpenTime = day.OpenTime;
            current.CloseTime = day.CloseTime;
            current.IsClosed = day.IsClosed;
        }

        db.BusinessHours.RemoveRange(existing.Where(h => schedule.All(day => day.DayOfWeek != h.DayOfWeek)));

        // A single SaveChanges runs in one transaction: the week is replaced completely or not at all.
        await db.SaveChangesAsync(ct);

        // Re-read instead of returning the tracked entities: updated_at is set by a Postgres trigger.
        return await db.BusinessHours.AsNoTracking()
            .Where(h => h.BusinessId == businessId)
            .OrderBy(h => h.DayOfWeek)
            .ToListAsync(ct);
    }

    public Task<BusinessLocation?> GetLocationAsync(Guid businessId, CancellationToken ct = default) =>
        db.BusinessLocations.AsNoTracking().FirstOrDefaultAsync(l => l.BusinessId == businessId, ct);

    public async Task<BusinessLocation> UpsertLocationAsync(BusinessLocation location, CancellationToken ct = default)
    {
        var existing = await db.BusinessLocations.FirstOrDefaultAsync(l => l.BusinessId == location.BusinessId, ct);
        if (existing is not null)
            return await ReplaceLocationAsync(existing, location, ct);

        db.BusinessLocations.Add(location);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_business_locations_business_id"))
        {
            // Lost the race against a concurrent first PUT for the same business: its row is
            // there now, so this one becomes a replace of it (last write wins, as for any PUT).
            db.Entry(location).State = EntityState.Detached;
            existing = await db.BusinessLocations.FirstAsync(l => l.BusinessId == location.BusinessId, ct);
            return await ReplaceLocationAsync(existing, location, ct);
        }

        await db.Entry(location).ReloadAsync(ct);
        return location;
    }

    private async Task<BusinessLocation> ReplaceLocationAsync(
        BusinessLocation existing, BusinessLocation values, CancellationToken ct)
    {
        existing.Address = values.Address;
        existing.City = values.City;
        existing.Province = values.Province;
        existing.Country = values.Country;
        existing.PostalCode = values.PostalCode;
        existing.Location = values.Location;

        await db.SaveChangesAsync(ct);
        await db.Entry(existing).ReloadAsync(ct);
        return existing;
    }

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraintName;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
