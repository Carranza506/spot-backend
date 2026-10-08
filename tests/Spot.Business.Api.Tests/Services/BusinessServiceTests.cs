using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Services;

public sealed class BusinessServiceTests
{
    private readonly FakeBusinessRepository _repository = new();
    private readonly FakeCategoryRepository _categoryRepository = new();
    private readonly BusinessService _service;

    public BusinessServiceTests() => _service = new BusinessService(_repository, _categoryRepository);

    [Fact]
    public async Task CreateAsync_SlugTaken_AppendsNextFreeSuffix()
    {
        _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" });
        _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella-2" });

        var result = await _service.CreateAsync(Guid.NewGuid(), new BusinessCreateRequest { Name = "Bella" });

        Assert.Equal("bella-3", result.Slug);
    }

    [Fact]
    public async Task CreateAsync_SlugTakenConcurrently_RetriesWithNextSuffix()
    {
        _repository.SlugsTakenConcurrently.Add("bella");

        var result = await _service.CreateAsync(Guid.NewGuid(), new BusinessCreateRequest { Name = "Bella" });

        Assert.Equal("bella-2", result.Slug);
    }

    [Fact]
    public async Task CreateAsync_NormalizesBlankOptionalFieldsToNullAndTrims()
    {
        var result = await _service.CreateAsync(Guid.NewGuid(), new BusinessCreateRequest
        {
            Name = "  Bella  ",
            Description = "   ",
            Phone = " 2222-3344 ",
        });

        Assert.Equal("Bella", result.Name);
        Assert.Null(result.Description);
        Assert.Equal("2222-3344", result.Phone);
    }

    [Fact]
    public async Task CreateAsync_AccountAlreadyHasBusiness_Throws()
    {
        var accountId = Guid.NewGuid();
        _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Uno" });

        await Assert.ThrowsAsync<BusinessAlreadyExistsException>(
            () => _service.CreateAsync(accountId, new BusinessCreateRequest { Name = "Dos" }));
    }

    [Fact]
    public async Task UpdateAsync_OmittedFieldsAreLeftUntouched()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity
        {
            AccountId = accountId, Name = "Bella", Email = "a@b.cr", LegalName = "Bella S.A.",
        });

        await _service.UpdateAsync(business.Id, accountId, new BusinessUpdateRequest { Email = Optional<string?>.Of(null) });

        Assert.Null(business.Email);
        Assert.Equal("Bella S.A.", business.LegalName);
        Assert.Equal("Bella", business.Name);
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_Throws()
    {
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.UpdateAsync(business.Id, Guid.NewGuid(), new BusinessUpdateRequest { Name = "X" }));
    }

    // ---------- ListCategoriesAsync ----------

    [Fact]
    public async Task ListCategoriesAsync_BusinessWithCategories_ReturnsThemPaged()
    {
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var belleza = _categoryRepository.Seed(new Category { Name = "Belleza" });
        var salud = _categoryRepository.Seed(new Category { Name = "Salud" });
        _repository.SeedBusinessCategories(business.Id, belleza, salud);

        var result = await _service.ListCategoriesAsync(business.Id, page: 1, pageSize: 20);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Pagination.Total);
        Assert.Equal(["Belleza", "Salud"], result.Data.Select(c => c.Name));
    }

    [Fact]
    public async Task ListCategoriesAsync_UnknownBusiness_ReturnsNull()
    {
        var result = await _service.ListCategoriesAsync(Guid.NewGuid(), page: 1, pageSize: 20);

        Assert.Null(result);
    }

    // ---------- ReplaceCategoriesAsync ----------

    [Fact]
    public async Task ReplaceCategoriesAsync_Owner_ReplacesTheSetAndReturnsIt()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var oldCategory = _categoryRepository.Seed(new Category { Name = "Vieja" });
        _repository.SeedBusinessCategories(business.Id, oldCategory);
        var newCategory = _categoryRepository.Seed(new Category { Name = "Nueva" });
        _repository.SeedCategory(newCategory);

        var result = await _service.ReplaceCategoriesAsync(business.Id, accountId, [newCategory.Id]);

        Assert.NotNull(result);
        Assert.Equal(["Nueva"], result!.Select(c => c.Name));
    }

    [Fact]
    public async Task ReplaceCategoriesAsync_NonOwner_Throws()
    {
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var category = _categoryRepository.Seed(new Category { Name = "Belleza" });
        _repository.SeedCategory(category);

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.ReplaceCategoriesAsync(business.Id, Guid.NewGuid(), [category.Id]));
    }

    [Fact]
    public async Task ReplaceCategoriesAsync_UnknownCategoryId_ThrowsAndDoesNotReplace()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var existing = _categoryRepository.Seed(new Category { Name = "Vieja" });
        _repository.SeedBusinessCategories(business.Id, existing);

        await Assert.ThrowsAsync<CategoryNotFoundException>(
            () => _service.ReplaceCategoriesAsync(business.Id, accountId, [Guid.NewGuid()]));

        var (items, _) = await _repository.ListCategoriesAsync(business.Id, page: 1, pageSize: 20);
        Assert.Equal(["Vieja"], items.Select(c => c.Name));
    }

    [Fact]
    public async Task ReplaceCategoriesAsync_UnknownBusiness_ReturnsNull()
    {
        var category = _categoryRepository.Seed(new Category { Name = "Belleza" });

        var result = await _service.ReplaceCategoriesAsync(Guid.NewGuid(), Guid.NewGuid(), [category.Id]);

        Assert.Null(result);
    }
}
