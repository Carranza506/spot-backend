using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IBusinessContactRepository — no database involved. Mirrors the real
/// repository's ordering (primary first, then created_at, then id), its business scoping on
/// lookups, and its "at most one primary per business" rule on create.
/// </summary>
public sealed class FakeBusinessContactRepository : IBusinessContactRepository
{
    private readonly Dictionary<Guid, BusinessContact> _contacts = [];

    public IReadOnlyCollection<BusinessContact> All => _contacts.Values;

    public void Reset() => _contacts.Clear();

    /// <summary>Seeds a contact directly, bypassing CreateAsync — for test setup.</summary>
    public BusinessContact Seed(BusinessContact contact)
    {
        if (contact.Id == Guid.Empty)
            contact.Id = Guid.NewGuid();

        if (contact.CreatedAt == default)
            contact.CreatedAt = DateTimeOffset.UtcNow;

        _contacts[contact.Id] = contact;
        return contact;
    }

    public Task<(IReadOnlyList<BusinessContact> Items, int Total)> ListByBusinessAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _contacts.Values
            .Where(c => c.BusinessId == businessId)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToList();

        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(((IReadOnlyList<BusinessContact>)pageItems, all.Count));
    }

    public Task<BusinessContact?> GetByIdAsync(Guid businessId, Guid contactId, CancellationToken ct = default) =>
        Task.FromResult(_contacts.TryGetValue(contactId, out var c) && c.BusinessId == businessId ? c : null);

    public Task CreateAsync(BusinessContact contact, CancellationToken ct = default)
    {
        if (contact.IsPrimary)
        {
            foreach (var other in _contacts.Values.Where(c => c.BusinessId == contact.BusinessId))
                other.IsPrimary = false;
        }

        contact.Id = Guid.NewGuid();
        contact.CreatedAt = DateTimeOffset.UtcNow;
        _contacts[contact.Id] = contact;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(BusinessContact contact, CancellationToken ct = default)
    {
        _contacts.Remove(contact.Id);
        return Task.CompletedTask;
    }
}
