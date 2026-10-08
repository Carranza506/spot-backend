using Microsoft.EntityFrameworkCore;
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

    private async Task<BusinessEntity> CreateBusinessAsync(string slug)
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = slug, Slug = slug };
        await _repository.CreateAsync(business);
        return business;
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
