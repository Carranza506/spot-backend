using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;

namespace Spot.Business.Api.Tests.Services;

public class CategoryServiceTests
{
    [Fact]
    public async Task CreateAsync_RootCategory_PersistsWithNullParent()
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);

        var result = await service.CreateAsync(new CategoryCreateRequest { Name = "Belleza" });

        Assert.Null(result.ParentCategoryId);
        Assert.Equal("Belleza", result.Name);
    }

    [Fact]
    public async Task CreateAsync_TrimsNameAndNormalizesBlankDescriptionToNull()
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);

        var result = await service.CreateAsync(new CategoryCreateRequest { Name = "  Salud  ", Description = "   " });

        Assert.Equal("Salud", result.Name);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task CreateAsync_ValidSubcategory_PersistsUnderRootParent()
    {
        var repo = new FakeCategoryRepository();
        var root = repo.Seed(new Category { Name = "Salud" });
        var service = new CategoryService(repo);

        var result = await service.CreateAsync(new CategoryCreateRequest { Name = "Farmacia", ParentCategoryId = root.Id });

        Assert.Equal(root.Id, result.ParentCategoryId);
    }

    [Fact]
    public async Task CreateAsync_NonexistentParent_ThrowsCategoryNotFoundException()
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<CategoryNotFoundException>(
            () => service.CreateAsync(new CategoryCreateRequest { Name = "Farmacia", ParentCategoryId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task CreateAsync_ParentIsItselfASubcategory_ThrowsInvalidCategoryHierarchyException()
    {
        var repo = new FakeCategoryRepository();
        var root = repo.Seed(new Category { Name = "Salud" });
        var child = repo.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<InvalidCategoryHierarchyException>(
            () => service.CreateAsync(new CategoryCreateRequest { Name = "Nieta", ParentCategoryId = child.Id }));
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameUnderSameParent_ThrowsDuplicateCategoryException()
    {
        var repo = new FakeCategoryRepository();
        repo.Seed(new Category { Name = "Salud" });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<DuplicateCategoryException>(
            () => service.CreateAsync(new CategoryCreateRequest { Name = "Salud" }));
    }

    [Fact]
    public async Task CreateAsync_SameNameUnderDifferentParents_Succeeds()
    {
        var repo = new FakeCategoryRepository();
        var salud = repo.Seed(new Category { Name = "Salud" });
        var belleza = repo.Seed(new Category { Name = "Belleza" });
        repo.Seed(new Category { Name = "Farmacia", ParentCategoryId = salud.Id });
        var service = new CategoryService(repo);

        var result = await service.CreateAsync(new CategoryCreateRequest { Name = "Farmacia", ParentCategoryId = belleza.Id });

        Assert.Equal(belleza.Id, result.ParentCategoryId);
    }

    [Fact]
    public async Task UpdateAsync_NonexistentCategory_ReturnsNull()
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);

        var result = await service.UpdateAsync(Guid.NewGuid(), new CategoryUpdateRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_SelfAsParent_ThrowsInvalidCategoryHierarchyException()
    {
        var repo = new FakeCategoryRepository();
        var category = repo.Seed(new Category { Name = "Salud" });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<InvalidCategoryHierarchyException>(() => service.UpdateAsync(
            category.Id, new CategoryUpdateRequest { ParentCategoryId = Optional<Guid?>.Of(category.Id) }));
    }

    [Fact]
    public async Task UpdateAsync_CategoryWithChildrenBecomesSubcategory_ThrowsInvalidCategoryHierarchyException()
    {
        var repo = new FakeCategoryRepository();
        var root = repo.Seed(new Category { Name = "Salud" });
        repo.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var otherRoot = repo.Seed(new Category { Name = "Belleza" });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<InvalidCategoryHierarchyException>(() => service.UpdateAsync(
            root.Id, new CategoryUpdateRequest { ParentCategoryId = Optional<Guid?>.Of(otherRoot.Id) }));
    }

    [Fact]
    public async Task UpdateAsync_NewParentIsSubcategory_ThrowsInvalidCategoryHierarchyException()
    {
        var repo = new FakeCategoryRepository();
        var root = repo.Seed(new Category { Name = "Salud" });
        var child = repo.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var lonely = repo.Seed(new Category { Name = "Belleza" });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<InvalidCategoryHierarchyException>(() => service.UpdateAsync(
            lonely.Id, new CategoryUpdateRequest { ParentCategoryId = Optional<Guid?>.Of(child.Id) }));
    }

    [Fact]
    public async Task UpdateAsync_NewParentDoesNotExist_ThrowsCategoryNotFoundException()
    {
        var repo = new FakeCategoryRepository();
        var category = repo.Seed(new Category { Name = "Salud" });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<CategoryNotFoundException>(() => service.UpdateAsync(
            category.Id, new CategoryUpdateRequest { ParentCategoryId = Optional<Guid?>.Of(Guid.NewGuid()) }));
    }

    [Fact]
    public async Task UpdateAsync_RenameToDuplicateUnderSameParent_ThrowsDuplicateCategoryException()
    {
        var repo = new FakeCategoryRepository();
        repo.Seed(new Category { Name = "Salud" });
        var belleza = repo.Seed(new Category { Name = "Belleza" });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<DuplicateCategoryException>(() => service.UpdateAsync(
            belleza.Id, new CategoryUpdateRequest { Name = "Salud" }));
    }

    [Fact]
    public async Task UpdateAsync_RenameToSameName_DoesNotThrowSelfDuplicate()
    {
        var repo = new FakeCategoryRepository();
        var category = repo.Seed(new Category { Name = "Salud", Description = "Original" });
        var service = new CategoryService(repo);

        var result = await service.UpdateAsync(category.Id, new CategoryUpdateRequest { Name = "Salud" });

        Assert.NotNull(result);
        Assert.Equal("Salud", result!.Name);
    }

    [Fact]
    public async Task UpdateAsync_PromotesSubcategoryToRoot_WhenParentCategoryIdExplicitlyNull()
    {
        var repo = new FakeCategoryRepository();
        var root = repo.Seed(new Category { Name = "Salud" });
        var child = repo.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var service = new CategoryService(repo);

        var result = await service.UpdateAsync(child.Id, new CategoryUpdateRequest { ParentCategoryId = Optional<Guid?>.Of(null) });

        Assert.Null(result!.ParentCategoryId);
    }

    [Fact]
    public async Task UpdateAsync_OmittedParentCategoryId_LeavesParentUnchanged()
    {
        var repo = new FakeCategoryRepository();
        var root = repo.Seed(new Category { Name = "Salud" });
        var child = repo.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var service = new CategoryService(repo);

        var result = await service.UpdateAsync(child.Id, new CategoryUpdateRequest { Name = "Farmacia Central" });

        Assert.Equal(root.Id, result!.ParentCategoryId);
    }

    [Fact]
    public async Task UpdateAsync_ExplicitNullDescription_ClearsIt()
    {
        var repo = new FakeCategoryRepository();
        var category = repo.Seed(new Category { Name = "Salud", Description = "Algo" });
        var service = new CategoryService(repo);

        var result = await service.UpdateAsync(category.Id, new CategoryUpdateRequest { Description = Optional<string?>.Of(null) });

        Assert.Null(result!.Description);
    }

    [Fact]
    public async Task UpdateAsync_OmittedDescription_LeavesItUnchanged()
    {
        var repo = new FakeCategoryRepository();
        var category = repo.Seed(new Category { Name = "Salud", Description = "Algo" });
        var service = new CategoryService(repo);

        var result = await service.UpdateAsync(category.Id, new CategoryUpdateRequest { Name = "Salud 2" });

        Assert.Equal("Algo", result!.Description);
    }

    [Fact]
    public async Task UpdateAsync_IsActiveFalse_Deactivates()
    {
        var repo = new FakeCategoryRepository();
        var category = repo.Seed(new Category { Name = "Salud", IsActive = true });
        var service = new CategoryService(repo);

        var result = await service.UpdateAsync(category.Id, new CategoryUpdateRequest { IsActive = false });

        Assert.False(result!.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_NonexistentCategory_ReturnsFalse()
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);

        Assert.False(await service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithSubcategories_ThrowsCategoryHasSubcategoriesException()
    {
        var repo = new FakeCategoryRepository();
        var root = repo.Seed(new Category { Name = "Salud" });
        repo.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<CategoryHasSubcategoriesException>(() => service.DeleteAsync(root.Id));
    }

    [Fact]
    public async Task DeleteAsync_LeafCategory_ReturnsTrue()
    {
        var repo = new FakeCategoryRepository();
        var category = repo.Seed(new Category { Name = "Salud" });
        var service = new CategoryService(repo);

        Assert.True(await service.DeleteAsync(category.Id));
    }

    [Fact]
    public async Task ListAsync_ComputesPaginationMetadata()
    {
        var repo = new FakeCategoryRepository();
        for (var i = 0; i < 5; i++)
            repo.Seed(new Category { Name = $"Categoria {i}" });
        var service = new CategoryService(repo);

        var result = await service.ListAsync(page: 1, pageSize: 2, ParentCategoryFilter.Any, null);

        Assert.Equal(2, result.Data.Count);
        Assert.Equal(5, result.Pagination.Total);
        Assert.Equal(3, result.Pagination.TotalPages);
    }
}
