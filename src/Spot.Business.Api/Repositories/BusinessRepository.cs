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
}
