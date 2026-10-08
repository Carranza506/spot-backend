using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Repositories;

/// <summary>
/// Real BusinessDbContext round-trips on EF Core's InMemory provider — see CategoryRepositoryTests
/// for why. The unique-index safety nets (23505 on IX_businesses_account_id / IX_businesses_slug)
/// can't be exercised here because InMemory never raises PostgresException; the account_id one is
/// covered by the Docker e2e run instead.
/// </summary>
public sealed class BusinessRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly BusinessRepository _repository;
    private readonly CategoryRepository _categoryRepository;

    public BusinessRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new BusinessRepository(_db);
        _categoryRepository = new CategoryRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_ThenLookups_FindTheBusinessByIdAccountAndSlug()
    {
        var accountId = Guid.NewGuid();
        var business = new BusinessEntity { AccountId = accountId, Name = "Bella", Slug = "bella" };

        await _repository.CreateAsync(business);

        Assert.NotNull(await _repository.GetByIdAsync(business.Id));
        Assert.Equal(business.Id, (await _repository.GetByAccountIdAsync(accountId))!.Id);
        Assert.True(await _repository.ExistsByAccountIdAsync(accountId));
        Assert.True(await _repository.SlugExistsAsync("bella"));
        Assert.False(await _repository.SlugExistsAsync("otra"));
    }

    [Fact]
    public async Task DeactivateThroughService_KeepsTheRowWithIsActiveFalse()
    {
        var accountId = Guid.NewGuid();
        var business = new BusinessEntity { AccountId = accountId, Name = "Bella", Slug = "bella" };
        await _repository.CreateAsync(business);
        var service = new BusinessService(_repository, _categoryRepository);

        var deactivated = await service.DeactivateAsync(business.Id, accountId);

        Assert.True(deactivated);
        await using var freshContext = new BusinessDbContext(_options);
        var stored = await freshContext.Businesses.SingleAsync(b => b.Id == business.Id);
        Assert.False(stored.IsActive);
    }

    // ---------- ListCategoriesAsync ----------

    [Fact]
    public async Task ListCategoriesAsync_ReturnsAssignedCategoriesOrderedByNameAndPaged()
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        await _repository.CreateAsync(business);
        var zoo = await SeedCategoryAsync("Zoologia");
        var arte = await SeedCategoryAsync("Arte");
        var otherBusiness = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro", Slug = "otro" };
        await _repository.CreateAsync(otherBusiness);
        var ajena = await SeedCategoryAsync("Ajena");
        await LinkAsync(business.Id, zoo.Id);
        await LinkAsync(business.Id, arte.Id);
        await LinkAsync(otherBusiness.Id, ajena.Id);

        var (items, total) = await _repository.ListCategoriesAsync(business.Id, page: 1, pageSize: 20);

        Assert.Equal(2, total);
        Assert.Equal(["Arte", "Zoologia"], items.Select(c => c.Name));
    }

    [Fact]
    public async Task ListCategoriesAsync_BusinessWithoutCategories_ReturnsEmpty()
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        await _repository.CreateAsync(business);

        var (items, total) = await _repository.ListCategoriesAsync(business.Id, page: 1, pageSize: 20);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    // ---------- ReplaceCategoriesAsync ----------

    [Fact]
    public async Task ReplaceCategoriesAsync_RemovesOldRowsAndInsertsTheNewSet()
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        await _repository.CreateAsync(business);
        var vieja = await SeedCategoryAsync("Vieja");
        var nueva = await SeedCategoryAsync("Nueva");
        await LinkAsync(business.Id, vieja.Id);

        var result = await _repository.ReplaceCategoriesAsync(business.Id, [nueva.Id]);

        Assert.Equal(["Nueva"], result.Select(c => c.Name));
        await using var freshDb = new BusinessDbContext(_options);
        var rows = await freshDb.BusinessCategories.Where(bc => bc.BusinessId == business.Id).ToListAsync();
        Assert.Equal([nueva.Id], rows.Select(bc => bc.CategoryId));
    }

    // ---------- Favorites ----------

    [Fact]
    public async Task ListFavoritesAsync_FiltersInactiveBeforePagingAndOrdersNewestFirst()
    {
        var userId = Guid.NewGuid();
        var oldest = await CreateBusinessAsync("oldest");
        var inactive = await CreateBusinessAsync("inactive", isActive: false);
        var newest = await CreateBusinessAsync("newest");
        var someoneElses = await CreateBusinessAsync("someone-elses");
        var t0 = DateTimeOffset.UtcNow;
        await SeedFavoriteAsync(userId, oldest.Id, t0);
        await SeedFavoriteAsync(userId, inactive.Id, t0.AddDays(1));
        await SeedFavoriteAsync(userId, newest.Id, t0.AddDays(2));
        await SeedFavoriteAsync(Guid.NewGuid(), someoneElses.Id, t0.AddDays(3));

        var (items, total) = await _repository.ListFavoritesAsync(userId, page: 1, pageSize: 1);

        Assert.Equal(2, total);
        var favorite = Assert.Single(items);
        Assert.Equal(newest.Id, favorite.BusinessId);
        Assert.Equal("newest", favorite.Business.Name);
    }

    [Fact]
    public async Task AddFavoriteAsync_AlreadyAFavorite_KeepsOneRowAndTheOriginalCreatedAt()
    {
        var userId = Guid.NewGuid();
        var business = await CreateBusinessAsync("bella");
        var original = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await SeedFavoriteAsync(userId, business.Id, original);

        await _repository.AddFavoriteAsync(userId, business.Id);

        await using var freshDb = new BusinessDbContext(_options);
        var row = await freshDb.FavoriteBusinesses.SingleAsync(f => f.UserId == userId);
        Assert.Equal(original, row.CreatedAt);
    }

    [Fact]
    public async Task AddFavoriteAsync_DuplicateKeyFromAConcurrentInsert_IsTreatedAsSuccess()
    {
        await using var racingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSaveInterceptor(PostgresErrorCodes.UniqueViolation))
            .Options);

        // The up-front AnyAsync sees no row, then SaveChanges loses the race (23505): no exception.
        await new BusinessRepository(racingDb).AddFavoriteAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(racingDb.ChangeTracker.Entries<FavoriteBusiness>());
    }

    [Fact]
    public async Task AddFavoriteAsync_OtherDatabaseError_IsNotSwallowed()
    {
        await using var failingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSaveInterceptor(PostgresErrorCodes.ForeignKeyViolation))
            .Options);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => new BusinessRepository(failingDb).AddFavoriteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveFavoriteAsync_DeletesOnlyTheCallersRow()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var business = await CreateBusinessAsync("bella");
        await SeedFavoriteAsync(userId, business.Id, DateTimeOffset.UtcNow);
        await SeedFavoriteAsync(otherUserId, business.Id, DateTimeOffset.UtcNow);

        await _repository.RemoveFavoriteAsync(userId, business.Id);
        await _repository.RemoveFavoriteAsync(userId, Guid.NewGuid());

        await using var freshDb = new BusinessDbContext(_options);
        var rows = await freshDb.FavoriteBusinesses.ToListAsync();
        Assert.Equal([otherUserId], rows.Select(f => f.UserId));
    }

    // ---------- ListHoursAsync / ReplaceHoursAsync ----------

    [Fact]
    public async Task ListHoursAsync_OrdersByDayOfWeekAndPagesWithinTheBusiness()
    {
        var business = await CreateBusinessAsync("bella");
        var other = await CreateBusinessAsync("otra");
        await SeedHoursAsync(business.Id, 4, 0, 2);
        await SeedHoursAsync(other.Id, 1);

        var (items, total) = await _repository.ListHoursAsync(business.Id, page: 1, pageSize: 2);

        Assert.Equal(3, total);
        Assert.Equal([0, 2], items.Select(h => (int)h.DayOfWeek));
    }

    [Fact]
    public async Task ReplaceHoursAsync_UpdatesInPlaceInsertsNewDaysAndDeletesMissingOnes()
    {
        var business = await CreateBusinessAsync("bella");
        var other = await CreateBusinessAsync("otra");
        await SeedHoursAsync(business.Id, 1, 2);
        await SeedHoursAsync(other.Id, 2);
        var mondayId = (await _db.BusinessHours.SingleAsync(h => h.BusinessId == business.Id && h.DayOfWeek == 1)).Id;

        var result = await _repository.ReplaceHoursAsync(business.Id,
        [
            new BusinessHours { DayOfWeek = 5, IsClosed = true },
            new BusinessHours { DayOfWeek = 1, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(12, 0) },
        ]);

        Assert.Equal([1, 5], result.Select(h => (int)h.DayOfWeek));
        await using var freshDb = new BusinessDbContext(_options);
        var rows = await freshDb.BusinessHours.Where(h => h.BusinessId == business.Id).OrderBy(h => h.DayOfWeek).ToListAsync();
        Assert.Equal([1, 5], rows.Select(h => (int)h.DayOfWeek));
        Assert.Equal(mondayId, rows[0].Id);
        Assert.Equal(new TimeOnly(8, 0), rows[0].OpenTime);
        Assert.Equal(1, await freshDb.BusinessHours.CountAsync(h => h.BusinessId == other.Id));
    }

    private async Task<BusinessEntity> CreateBusinessAsync(string slug, bool isActive = true)
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = slug, Slug = slug, IsActive = isActive };
        await _repository.CreateAsync(business);
        return business;
    }

    private async Task SeedFavoriteAsync(Guid userId, Guid businessId, DateTimeOffset createdAt)
    {
        _db.FavoriteBusinesses.Add(new FavoriteBusiness { UserId = userId, BusinessId = businessId, CreatedAt = createdAt });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// InMemory never raises PostgresException, so this stands in for Postgres rejecting the insert
    /// with <paramref name="sqlState"/> — e.g. 23505 when a concurrent request inserted the same favorite first.
    /// </summary>
    private sealed class ThrowOnSaveInterceptor(string sqlState) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database error.", new PostgresException("simulated", "ERROR", "ERROR", sqlState));
    }

    private async Task SeedHoursAsync(Guid businessId, params int[] days)
    {
        _db.BusinessHours.AddRange(days.Select(day => new BusinessHours
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            DayOfWeek = (short)day,
            IsClosed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }));
        await _db.SaveChangesAsync();
    }

    private async Task<Category> SeedCategoryAsync(string name)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    private async Task LinkAsync(Guid businessId, Guid categoryId)
    {
        _db.BusinessCategories.Add(new BusinessCategory { BusinessId = businessId, CategoryId = categoryId, CreatedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();
    }
}
