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

    // ---------- SearchAsync (no `q`: EF.Functions.ILike isn't translated by the InMemory provider,
    // so the q/ILIKE path is covered in BusinessServiceTests via the fake instead) ----------

    [Fact]
    public async Task SearchAsync_FiltersByCategory()
    {
        var bella = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        var otro = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro", Slug = "otro" };
        await _repository.CreateAsync(bella);
        await _repository.CreateAsync(otro);
        var belleza = await SeedCategoryAsync("Belleza");
        await LinkAsync(bella.Id, belleza.Id);

        var (items, total) = await _repository.SearchAsync(null, belleza.Id, null, null, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal(["Bella"], items.Select(b => b.Name));
    }

    [Fact]
    public async Task SearchAsync_FiltersByCity_ExcludesBusinessWithoutLocation()
    {
        var conUbicacion = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Con ubicación", Slug = "con" };
        var sinUbicacion = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Sin ubicación", Slug = "sin" };
        await _repository.CreateAsync(conUbicacion);
        await _repository.CreateAsync(sinUbicacion);
        await SeedLocationAsync(conUbicacion.Id, city: "San José", province: "San José");

        var (items, total) = await _repository.SearchAsync(null, null, "San José", null, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal(["Con ubicación"], items.Select(b => b.Name));
    }

    [Fact]
    public async Task SearchAsync_NoFilters_IncludesBusinessWithoutLocationButExcludesInactive()
    {
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Activo", Slug = "activo" });
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Inactivo", Slug = "inactivo", IsActive = false });

        var (items, total) = await _repository.SearchAsync(null, null, null, null, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal(["Activo"], items.Select(b => b.Name));
    }

    private async Task SeedLocationAsync(Guid businessId, string? city = null, string? province = null)
    {
        _db.BusinessLocations.Add(new BusinessLocation
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Address = "Calle 1",
            City = city,
            Province = province,
            Location = "POINT(0 0)",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
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
