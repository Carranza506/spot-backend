using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Services;

public sealed class BusinessServiceTests
{
    private readonly FakeBusinessRepository _repository = new();
    private readonly BusinessService _service;

    public BusinessServiceTests() => _service = new BusinessService(_repository);

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
