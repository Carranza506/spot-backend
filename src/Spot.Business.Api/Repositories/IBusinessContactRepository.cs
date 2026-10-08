using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public interface IBusinessContactRepository
{
    /// <summary>
    /// Lists one business's contacts, paged, ordered primary first, then by creation time, then
    /// by id (so pages are stable even when two contacts share a created_at).
    /// </summary>
    Task<(IReadOnlyList<BusinessContact> Items, int Total)> ListByBusinessAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Loads a contact by id, but only if it belongs to <paramref name="businessId"/> — a
    /// contact of another business is reported as missing (null). The returned entity is tracked.
    /// </summary>
    Task<BusinessContact?> GetByIdAsync(Guid businessId, Guid contactId, CancellationToken ct = default);

    /// <summary>
    /// Inserts <paramref name="contact"/>. If it's primary, every other contact of the same
    /// business loses its primary flag in the same SaveChanges (one transaction), so a business
    /// has at most one primary contact.
    /// </summary>
    Task CreateAsync(BusinessContact contact, CancellationToken ct = default);

    /// <summary>Hard delete — the row is removed permanently.</summary>
    Task DeleteAsync(BusinessContact contact, CancellationToken ct = default);
}
