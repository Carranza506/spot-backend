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

    // Stands in for the real repository's shared BusinessDbContext (which reaches Categories
    // through the same table business_categories joins against) — the fake needs its own registry
    // to resolve a categoryId back into a Category for ReplaceCategoriesAsync's return value.
    private readonly Dictionary<Guid, Category> _knownCategories = [];
    private readonly Dictionary<Guid, List<Category>> _businessCategories = [];

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
        _knownCategories.Clear();
        _businessCategories.Clear();
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

    /// <summary>Registers a category so ReplaceCategoriesAsync can resolve it by id — for test setup.</summary>
    public Category SeedCategory(Category category)
    {
        if (category.Id == Guid.Empty)
            category.Id = Guid.NewGuid();

        _knownCategories[category.Id] = category;
        return category;
    }

    /// <summary>Seeds the categories currently assigned to a business — for GET-list test setup.</summary>
    public void SeedBusinessCategories(Guid businessId, params Category[] categories)
    {
        foreach (var category in categories)
            SeedCategory(category);

        _businessCategories[businessId] = [.. categories];
    }

    public Task<(IReadOnlyList<Category> Items, int Total)> ListCategoriesAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _businessCategories.GetValueOrDefault(businessId, []).OrderBy(c => c.Name).ToList();
        var paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(((IReadOnlyList<Category>)paged, all.Count));
    }

    public Task<IReadOnlyList<Category>> ReplaceCategoriesAsync(
        Guid businessId, IReadOnlyCollection<Guid> categoryIds, CancellationToken ct = default)
    {
        var categories = categoryIds.Select(id => _knownCategories[id]).OrderBy(c => c.Name).ToList();
        _businessCategories[businessId] = categories;

        return Task.FromResult((IReadOnlyList<Category>)categories);
    }

    public Task<(IReadOnlyList<BusinessEntity> Items, int Total)> SearchAsync(
        string? q, Guid? categoryId, string? city, string? province,
        int page, int pageSize, CancellationToken ct = default)
    {
        // Mirrors BusinessRepository.SearchAsync: active only, AND of the given filters, ordered by name.
        IEnumerable<BusinessEntity> matches = _businesses.Values.Where(b => b.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            matches = matches.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (b.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (categoryId is { } catId)
            matches = matches.Where(b => _businessCategories.GetValueOrDefault(b.Id, []).Any(c => c.Id == catId));

        // A business with no location is only excluded when city/province is actually filtered on.
        if (!string.IsNullOrWhiteSpace(city))
            matches = matches.Where(b => b.Location?.City == city.Trim());

        if (!string.IsNullOrWhiteSpace(province))
            matches = matches.Where(b => b.Location?.Province == province.Trim());

        var all = matches.OrderBy(b => b.Name).ToList();
        var paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(((IReadOnlyList<BusinessEntity>)paged, all.Count));
    }
}
