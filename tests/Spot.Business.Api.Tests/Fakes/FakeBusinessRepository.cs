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
    private readonly List<FavoriteBusiness> _favorites = [];
    private readonly Dictionary<Guid, List<BusinessHours>> _businessHours = [];

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
        _knownCategories.Clear();
        _businessCategories.Clear();
        _favorites.Clear();
        _businessHours.Clear();
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

    /// <summary>Seeds a favorite with an explicit created_at (to control ordering) — for test setup. The business must be seeded first.</summary>
    public FavoriteBusiness SeedFavorite(Guid userId, Guid businessId, DateTimeOffset createdAt)
    {
        var favorite = new FavoriteBusiness
        {
            UserId = userId,
            BusinessId = businessId,
            CreatedAt = createdAt,
            Business = _businesses[businessId],
        };
        _favorites.Add(favorite);
        return favorite;
    }

    /// <summary>Every stored favorite row of a user, active business or not — for assertions.</summary>
    public IReadOnlyList<FavoriteBusiness> FavoritesOf(Guid userId) => _favorites.Where(f => f.UserId == userId).ToList();

    public Task<(IReadOnlyList<FavoriteBusiness> Items, int Total)> ListFavoritesAsync(
        Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _favorites
            .Where(f => f.UserId == userId && _businesses[f.BusinessId].IsActive)
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.BusinessId)
            .ToList();
        var paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(((IReadOnlyList<FavoriteBusiness>)paged, all.Count));
    }

    public Task AddFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        if (!_favorites.Any(f => f.UserId == userId && f.BusinessId == businessId))
            SeedFavorite(userId, businessId, DateTimeOffset.UtcNow);

        return Task.CompletedTask;
    }

    public Task RemoveFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        _favorites.RemoveAll(f => f.UserId == userId && f.BusinessId == businessId);
        return Task.CompletedTask;
    }

    /// <summary>Seeds the weekly schedule currently stored for a business — for test setup.</summary>
    public void SeedHours(Guid businessId, params BusinessHours[] hours)
    {
        foreach (var day in hours)
        {
            if (day.Id == Guid.Empty)
                day.Id = Guid.NewGuid();

            day.BusinessId = businessId;
            day.CreatedAt = DateTimeOffset.UtcNow;
            day.UpdatedAt = day.CreatedAt;
        }

        _businessHours[businessId] = [.. hours];
    }

    /// <summary>The schedule currently stored for a business, ordered by day — for assertions.</summary>
    public IReadOnlyList<BusinessHours> HoursOf(Guid businessId) =>
        _businessHours.GetValueOrDefault(businessId, []).OrderBy(h => h.DayOfWeek).ToList();

    public Task<(IReadOnlyList<BusinessHours> Items, int Total)> ListHoursAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var all = HoursOf(businessId);
        var paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(((IReadOnlyList<BusinessHours>)paged, all.Count));
    }

    /// <summary>Same semantics as the real repository: stored days keep their id and created_at, missing days are dropped.</summary>
    public Task<IReadOnlyList<BusinessHours>> ReplaceHoursAsync(
        Guid businessId, IReadOnlyCollection<BusinessHours> schedule, CancellationToken ct = default)
    {
        var existing = _businessHours.GetValueOrDefault(businessId, []);
        var now = DateTimeOffset.UtcNow;

        var replaced = schedule.Select(day =>
        {
            var current = existing.FirstOrDefault(h => h.DayOfWeek == day.DayOfWeek);
            if (current is null)
            {
                day.Id = Guid.NewGuid();
                day.BusinessId = businessId;
                day.CreatedAt = now;
                day.UpdatedAt = now;
                return day;
            }

            current.OpenTime = day.OpenTime;
            current.CloseTime = day.CloseTime;
            current.IsClosed = day.IsClosed;
            current.UpdatedAt = now;
            return current;
        }).ToList();

        _businessHours[businessId] = replaced;

        return Task.FromResult(HoursOf(businessId));
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
