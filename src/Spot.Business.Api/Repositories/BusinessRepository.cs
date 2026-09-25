using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Business.Api.Data;
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

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraintName;
}
