using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IBusinessRepository — no database involved. Shared between
/// BusinessServiceTests (direct construction) and BusinessesApiFactory (DI-swapped), same as
/// FakeCategoryRepository. Simulates the real repository's two unique indexes (account_id, slug)
/// so callers see the same exceptions as against Postgres.
/// </summary>
public sealed class FakeBusinessRepository : IBusinessRepository
{
    private readonly Dictionary<Guid, BusinessEntity> _businesses = [];

    /// <summary>Keyed by business id — simulates the unique business_locations.business_id.</summary>
    private readonly Dictionary<Guid, BusinessLocation> _locations = [];

    /// <summary>
    /// Slugs that SlugExistsAsync reports as free but CreateAsync rejects — simulates losing the
    /// race against a concurrent create between the up-front check and the insert.
    /// </summary>
    public HashSet<string> SlugsTakenConcurrently { get; } = [];

    public IReadOnlyCollection<BusinessEntity> All => _businesses.Values;

    public void Reset()
    {
        _businesses.Clear();
        SlugsTakenConcurrently.Clear();
        _locations.Clear();
    }

    /// <summary>Seeds a business directly, bypassing CreateAsync — for test setup.</summary>
    public BusinessEntity Seed(BusinessEntity business)
    {
        if (business.Id == Guid.Empty)
            business.Id = Guid.NewGuid();

        if (string.IsNullOrEmpty(business.Slug))
            business.Slug = $"seeded-{business.Id:N}";

        business.CreatedAt = DateTimeOffset.UtcNow;
        business.UpdatedAt = business.CreatedAt;
        _businesses[business.Id] = business;
        return business;
    }

    public Task<BusinessEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_businesses.GetValueOrDefault(id));

    public Task<BusinessEntity?> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_businesses.Values.FirstOrDefault(b => b.AccountId == accountId));

    public Task<bool> ExistsByAccountIdAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_businesses.Values.Any(b => b.AccountId == accountId));

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(_businesses.Values.Any(b => b.Slug == slug));

    public Task CreateAsync(BusinessEntity business, CancellationToken ct = default)
    {
        if (_businesses.Values.Any(b => b.AccountId == business.AccountId))
            throw new BusinessAlreadyExistsException(business.AccountId);

        if (SlugsTakenConcurrently.Remove(business.Slug) || _businesses.Values.Any(b => b.Slug == business.Slug))
            throw new DuplicateBusinessSlugException(business.Slug);

        business.Id = Guid.NewGuid();
        business.CreatedAt = DateTimeOffset.UtcNow;
        business.UpdatedAt = business.CreatedAt;
        _businesses[business.Id] = business;

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(BusinessEntity business, CancellationToken ct = default)
    {
        business.UpdatedAt = DateTimeOffset.UtcNow;
        _businesses[business.Id] = business;
        return Task.CompletedTask;
    }

    public Task<BusinessLocation?> GetLocationAsync(Guid businessId, CancellationToken ct = default) =>
        Task.FromResult(_locations.GetValueOrDefault(businessId));

    /// <summary>Same contract as the real one: a replace keeps the existing row's id and createdAt.</summary>
    public Task<BusinessLocation> UpsertLocationAsync(BusinessLocation location, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (_locations.TryGetValue(location.BusinessId, out var existing))
        {
            location.Id = existing.Id;
            location.CreatedAt = existing.CreatedAt;
        }
        else
        {
            location.Id = Guid.NewGuid();
            location.CreatedAt = now;
        }

        location.UpdatedAt = now;
        _locations[location.BusinessId] = location;

        return Task.FromResult(location);
    }
}
