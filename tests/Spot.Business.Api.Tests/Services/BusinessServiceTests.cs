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

    [Fact]
    public async Task ListFavoritesAsync_MapsTheFavoriteAndItsBusiness()
    {
        var userId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Email = "hola@bella.cr" });
        var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        _repository.SeedFavorite(userId, business.Id, createdAt);

        var result = await _service.ListFavoritesAsync(userId, page: 1, pageSize: 20);

        var favorite = Assert.Single(result.Data);
        Assert.Equal(business.Id, favorite.BusinessId);
        Assert.Equal(createdAt, favorite.CreatedAt);
        Assert.Equal("hola@bella.cr", favorite.Business.Email);
        Assert.Equal(1, result.Pagination.Total);
    }

    [Fact]
    public async Task AddFavoriteAsync_ActiveBusiness_ReturnsTrueAndSavesIt()
    {
        var userId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });

        Assert.True(await _service.AddFavoriteAsync(userId, business.Id));
        Assert.Single(_repository.FavoritesOf(userId));
    }

    [Fact]
    public async Task AddFavoriteAsync_InactiveBusinessAlreadyAFavorite_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", IsActive = false });
        _repository.SeedFavorite(userId, business.Id, DateTimeOffset.UtcNow);

        Assert.False(await _service.AddFavoriteAsync(userId, business.Id));
    }

    [Fact]
    public async Task AddFavoriteAsync_UnknownBusiness_ReturnsFalseAndSavesNothing()
    {
        var userId = Guid.NewGuid();

        Assert.False(await _service.AddFavoriteAsync(userId, Guid.NewGuid()));
        Assert.Empty(_repository.FavoritesOf(userId));
    }

    [Fact]
    public async Task ListHoursAsync_InactiveBusiness_ReturnsNull()
    {
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", IsActive = false });
        _repository.SeedHours(business.Id, new BusinessHours { DayOfWeek = 1, IsClosed = true });

        var result = await _service.ListHoursAsync(business.Id, page: 1, pageSize: 20);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReplaceHoursAsync_Owner_UpdatesStoredDaysInPlaceAndDropsMissingOnes()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        _repository.SeedHours(business.Id,
            new BusinessHours { DayOfWeek = 1, IsClosed = true },
            new BusinessHours { DayOfWeek = 2, IsClosed = true });
        var mondayId = _repository.HoursOf(business.Id)[0].Id;

        var result = await _service.ReplaceHoursAsync(business.Id, accountId,
            [new BusinessHourInput { DayOfWeek = 1, IsClosed = false, OpenTime = "09:00:00", CloseTime = "18:00:00" }]);

        var monday = Assert.Single(result!);
        Assert.Equal(mondayId, monday.Id);
        Assert.Equal("09:00:00", monday.OpenTime);
        Assert.False(monday.IsClosed);
        Assert.Equal([1], _repository.HoursOf(business.Id).Select(h => (int)h.DayOfWeek));
    }

    [Fact]
    public async Task ReplaceHoursAsync_NonOwner_ThrowsAndKeepsTheSchedule()
    {
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        _repository.SeedHours(business.Id, new BusinessHours { DayOfWeek = 3, IsClosed = true });

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(() => _service.ReplaceHoursAsync(
            business.Id, Guid.NewGuid(), [new BusinessHourInput { DayOfWeek = 1, IsClosed = true }]));

        Assert.Equal(3, _repository.HoursOf(business.Id).Single().DayOfWeek);
    }

    [Fact]
    public async Task ReplaceHoursAsync_UnknownBusiness_ReturnsNull()
    {
        var result = await _service.ReplaceHoursAsync(
            Guid.NewGuid(), Guid.NewGuid(), [new BusinessHourInput { DayOfWeek = 1, IsClosed = true }]);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReplaceHoursAsync_DuplicateDayAndInvalidRange_ReportsTheDuplicateFirst()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });

        var ex = await Assert.ThrowsAsync<InvalidBusinessHoursException>(() => _service.ReplaceHoursAsync(business.Id, accountId,
        [
            new BusinessHourInput { DayOfWeek = 1, IsClosed = false, OpenTime = "18:00:00", CloseTime = "09:00:00" },
            new BusinessHourInput { DayOfWeek = 1, IsClosed = true },
        ]));

        Assert.Equal(InvalidBusinessHoursException.DuplicateDayOfWeek, ex.Code);
        Assert.Empty(_repository.HoursOf(business.Id));
    }

    // ---------- GetPublicLocationAsync ----------

    [Fact]
    public async Task GetPublicLocationAsync_UnknownBusiness_ReturnsNull()
    {
        Assert.Null(await _service.GetPublicLocationAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPublicLocationAsync_InactiveBusiness_ReturnsNullEvenWithALocation()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        await _service.UpsertLocationAsync(business.Id, accountId, LocationRequest());
        business.IsActive = false;

        Assert.Null(await _service.GetPublicLocationAsync(business.Id));
    }

    [Fact]
    public async Task GetPublicLocationAsync_BusinessWithoutLocation_Throws()
    {
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });

        await Assert.ThrowsAsync<BusinessLocationNotSetException>(() => _service.GetPublicLocationAsync(business.Id));
    }

    // ---------- UpsertLocationAsync ----------

    [Fact]
    public async Task UpsertLocationAsync_Owner_NormalizesTextAndDefaultsCountry()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });

        var result = await _service.UpsertLocationAsync(business.Id, accountId, LocationRequest(
            address: "  200 m norte de la iglesia  ", city: "   ", province: " San José ", country: null));

        Assert.NotNull(result);
        Assert.Equal("200 m norte de la iglesia", result!.Address);
        Assert.Null(result.City);
        Assert.Equal("San José", result.Province);
        Assert.Equal("Costa Rica", result.Country);
        Assert.Equal(new GeoPointDto(9.9325, -84.0795), result.Location);
    }

    [Fact]
    public async Task UpsertLocationAsync_SecondCall_ReplacesKeepingTheSameId()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var first = await _service.UpsertLocationAsync(business.Id, accountId, LocationRequest());

        var second = await _service.UpsertLocationAsync(business.Id, accountId, LocationRequest(
            address: "Nuevo local", country: "Panamá", latitude: 8.9824, longitude: -79.5199));

        Assert.Equal(first!.Id, second!.Id);
        var stored = await _service.GetPublicLocationAsync(business.Id);
        Assert.Equal("Nuevo local", stored!.Address);
        Assert.Equal("Panamá", stored.Country);
        Assert.Equal(new GeoPointDto(8.9824, -79.5199), stored.Location);
    }

    [Fact]
    public async Task UpsertLocationAsync_InactiveBusiness_StillAllowedForTheOwner()
    {
        var accountId = Guid.NewGuid();
        var business = _repository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });

        Assert.NotNull(await _service.UpsertLocationAsync(business.Id, accountId, LocationRequest()));
    }

    [Fact]
    public async Task UpsertLocationAsync_NonOwner_ThrowsAndStoresNothing()
    {
        var business = _repository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.UpsertLocationAsync(business.Id, Guid.NewGuid(), LocationRequest()));
        Assert.Null(await _repository.GetLocationAsync(business.Id));
    }

    [Fact]
    public async Task UpsertLocationAsync_UnknownBusiness_ReturnsNull()
    {
        Assert.Null(await _service.UpsertLocationAsync(Guid.NewGuid(), Guid.NewGuid(), LocationRequest()));
    }

    private static BusinessLocationUpsertRequest LocationRequest(
        string address = "200 m norte de la iglesia",
        string? city = "San José",
        string? province = null,
        string? country = null,
        double latitude = 9.9325,
        double longitude = -84.0795) => new()
    {
        Address = address,
        City = city,
        Province = province,
        Country = country,
        Location = new GeoPointRequest { Latitude = latitude, Longitude = longitude },
    };
}
