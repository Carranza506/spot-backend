using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Repositories;

/// <summary>
/// Integration tests against a real BusinessDbContext/SaveChangesAsync round-trip, using EF
/// Core's InMemory provider — see Spot.Auth.Api.Tests' RefreshTokenRepositoryTests for why
/// that's an equivalent, Postgres-independent choice for these entities. The DB-level safety nets
/// in CategoryRepository (the DbUpdateException catch clauses for the unique index and the
/// ON DELETE RESTRICT foreign key) aren't exercised here for the same reason: InMemory never
/// raises real PostgresException instances. Those two paths were verified manually against a
/// real Postgres instance instead — see PR notes.
/// </summary>
public sealed class CategoryRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly CategoryRepository _repository;

    public CategoryRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new CategoryRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsAllCategoriesOrderedByName()
    {
        await SeedAsync(new Category { Name = "Zoologia" });
        await SeedAsync(new Category { Name = "Arte" });

        var (items, total) = await _repository.ListAsync(ParentCategoryFilter.Any, null, page: 1, pageSize: 20);

        Assert.Equal(2, total);
        Assert.Equal(["Arte", "Zoologia"], items.Select(c => c.Name));
    }

    [Fact]
    public async Task ListAsync_RootOnly_ExcludesSubcategories()
    {
        var root = await SeedAsync(new Category { Name = "Salud" });
        await SeedAsync(new Category { Name = "Farmacia", ParentCategoryId = root.Id });

        var (items, total) = await _repository.ListAsync(ParentCategoryFilter.RootOnly, null, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal("Salud", items.Single().Name);
    }

    [Fact]
    public async Task ListAsync_SpecificParent_ReturnsOnlyThatParentsDirectChildren()
    {
        var root = await SeedAsync(new Category { Name = "Salud" });
        var otherRoot = await SeedAsync(new Category { Name = "Belleza" });
        await SeedAsync(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        await SeedAsync(new Category { Name = "Peluqueria", ParentCategoryId = otherRoot.Id });

        var (items, total) = await _repository.ListAsync(ParentCategoryFilter.Specific, root.Id, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal("Farmacia", items.Single().Name);
    }

    [Fact]
    public async Task ListAsync_Pagination_SkipsAndTakesCorrectly()
    {
        for (var i = 0; i < 5; i++)
            await SeedAsync(new Category { Name = $"Cat{i:D2}" });

        var (items, total) = await _repository.ListAsync(ParentCategoryFilter.Any, null, page: 2, pageSize: 2);

        Assert.Equal(5, total);
        Assert.Equal(["Cat02", "Cat03"], items.Select(c => c.Name));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingCategory_ReturnsItWithSubCategories()
    {
        var root = await SeedAsync(new Category { Name = "Salud" });
        await SeedAsync(new Category { Name = "Farmacia", ParentCategoryId = root.Id });

        var found = await _repository.GetByIdAsync(root.Id);

        Assert.NotNull(found);
        Assert.Single(found!.SubCategories);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExistsByParentAndNameAsync_MatchingNameUnderSameParent_ReturnsTrue()
    {
        var root = await SeedAsync(new Category { Name = "Salud" });
        await SeedAsync(new Category { Name = "Farmacia", ParentCategoryId = root.Id });

        Assert.True(await _repository.ExistsByParentAndNameAsync(root.Id, "Farmacia"));
    }

    [Fact]
    public async Task ExistsByParentAndNameAsync_SameNameUnderDifferentParent_ReturnsFalse()
    {
        var root = await SeedAsync(new Category { Name = "Salud" });
        var otherRoot = await SeedAsync(new Category { Name = "Belleza" });
        await SeedAsync(new Category { Name = "Farmacia", ParentCategoryId = root.Id });

        Assert.False(await _repository.ExistsByParentAndNameAsync(otherRoot.Id, "Farmacia"));
    }

    [Fact]
    public async Task ExistsByParentAndNameAsync_ExcludingItsOwnId_ReturnsFalse()
    {
        var category = await SeedAsync(new Category { Name = "Salud" });

        Assert.False(await _repository.ExistsByParentAndNameAsync(null, "Salud", excludeId: category.Id));
    }

    [Fact]
    public async Task CreateAsync_PersistsTheCategoryAndGeneratesAnId()
    {
        var category = new Category { Name = "Salud" };

        await _repository.CreateAsync(category);

        Assert.NotEqual(Guid.Empty, category.Id);
        await using var freshDb = new BusinessDbContext(_options);
        Assert.NotNull(await freshDb.Categories.FindAsync(category.Id));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsMutationsMadeOnTheTrackedEntity()
    {
        var category = await SeedAsync(new Category { Name = "Salud" });
        var tracked = await _repository.GetByIdAsync(category.Id);
        tracked!.Name = "Salud y Bienestar";

        await _repository.SaveChangesAsync(tracked);

        await using var freshDb = new BusinessDbContext(_options);
        var persisted = await freshDb.Categories.SingleAsync(c => c.Id == category.Id);
        Assert.Equal("Salud y Bienestar", persisted.Name);
    }

    [Fact]
    public async Task DeleteAsync_LeafCategory_RemovesItAndReturnsTrue()
    {
        var category = await SeedAsync(new Category { Name = "Salud" });

        var deleted = await _repository.DeleteAsync(category.Id);

        Assert.True(deleted);
        Assert.Null(await _repository.GetByIdAsync(category.Id));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        Assert.False(await _repository.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithSubcategories_ThrowsAndDoesNotDelete()
    {
        var root = await SeedAsync(new Category { Name = "Salud" });
        await SeedAsync(new Category { Name = "Farmacia", ParentCategoryId = root.Id });

        await Assert.ThrowsAsync<CategoryHasSubcategoriesException>(() => _repository.DeleteAsync(root.Id));

        Assert.NotNull(await _repository.GetByIdAsync(root.Id));
    }

    private async Task<Category> SeedAsync(Category category)
    {
        if (category.Id == Guid.Empty)
            category.Id = Guid.NewGuid();

        category.CreatedAt = DateTimeOffset.UtcNow;
        category.UpdatedAt = category.CreatedAt;
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return category;
    }
}
