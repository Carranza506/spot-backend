using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public sealed class BusinessContactRepository(BusinessDbContext db) : IBusinessContactRepository
{
    public async Task<(IReadOnlyList<BusinessContact> Items, int Total)> ListByBusinessAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.BusinessContacts.AsNoTracking().Where(c => c.BusinessId == businessId);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<BusinessContact?> GetByIdAsync(Guid businessId, Guid contactId, CancellationToken ct = default) =>
        db.BusinessContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.BusinessId == businessId, ct);

    public async Task CreateAsync(BusinessContact contact, CancellationToken ct = default)
    {
        await ClearOtherPrimariesAsync(contact, ct);
        db.BusinessContacts.Add(contact);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(BusinessContact contact, CancellationToken ct = default)
    {
        await ClearOtherPrimariesAsync(contact, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ClearOtherPrimariesAsync(BusinessContact contact, CancellationToken ct)
    {
        if (!contact.IsPrimary)
            return;

        var currentPrimaries = await db.BusinessContacts
            .Where(c => c.BusinessId == contact.BusinessId && c.IsPrimary && c.Id != contact.Id)
            .ToListAsync(ct);

        foreach (var other in currentPrimaries)
            other.IsPrimary = false;
    }

    public async Task DeleteAsync(BusinessContact contact, CancellationToken ct = default)
    {
        db.BusinessContacts.Remove(contact);
        await db.SaveChangesAsync(ct);
    }
}
