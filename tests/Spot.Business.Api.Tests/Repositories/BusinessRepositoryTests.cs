using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
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
/// covered by the Docker e2e run instead. Same for UpsertLocationAsync's retry on
/// IX_business_locations_business_id.
/// </summary>
public sealed class BusinessRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly BusinessRepository _repository;

    public BusinessRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new BusinessRepository(_db);
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
        var service = new BusinessService(_repository);

        var deactivated = await service.DeactivateAsync(business.Id, accountId);

        Assert.True(deactivated);
        await using var freshContext = new BusinessDbContext(_options);
        var stored = await freshContext.Businesses.SingleAsync(b => b.Id == business.Id);
        Assert.False(stored.IsActive);
    }

    // ---------- GetLocationAsync / UpsertLocationAsync ----------

    [Fact]
    public async Task GetLocationAsync_BusinessWithoutLocation_ReturnsNull()
    {
        var business = await CreateBusinessAsync();

        Assert.Null(await _repository.GetLocationAsync(business.Id));
    }

    [Fact]
    public async Task UpsertLocationAsync_NoRowYet_InsertsIt()
    {
        var business = await CreateBusinessAsync();

        var stored = await _repository.UpsertLocationAsync(NewLocation(business.Id, "200 m norte", latitude: 9.9325, longitude: -84.0795));

        Assert.NotEqual(Guid.Empty, stored.Id);
        var found = await _repository.GetLocationAsync(business.Id);
        Assert.NotNull(found);
        Assert.Equal("200 m norte", found!.Address);
        // NetTopologySuite axis order: X = longitude, Y = latitude.
        Assert.Equal(-84.0795, found.Location.X);
        Assert.Equal(9.9325, found.Location.Y);
        Assert.Equal(4326, found.Location.SRID);
    }

    [Fact]
    public async Task UpsertLocationAsync_RowExists_ReplacesItKeepingIdAndASingleRow()
    {
        var business = await CreateBusinessAsync();
        var first = await _repository.UpsertLocationAsync(NewLocation(business.Id, "Viejo local", latitude: 9.9325, longitude: -84.0795, city: "San José"));
        var firstId = first.Id;
        _db.ChangeTracker.Clear();

        var second = await _repository.UpsertLocationAsync(NewLocation(business.Id, "Nuevo local", latitude: 9.9981, longitude: -84.1165));

        Assert.Equal(firstId, second.Id);
        await using var freshContext = new BusinessDbContext(_options);
        var stored = Assert.Single(await freshContext.BusinessLocations.Where(l => l.BusinessId == business.Id).ToListAsync());
        Assert.Equal("Nuevo local", stored.Address);
        Assert.Null(stored.City);
        Assert.Equal(9.9981, stored.Location.Y);
    }

    [Fact]
    public async Task UpsertLocationAsync_OnlyTouchesItsOwnBusiness()
    {
        var business = await CreateBusinessAsync("bella");
        var other = await CreateBusinessAsync("otro");
        await _repository.UpsertLocationAsync(NewLocation(other.Id, "Del otro", latitude: 10, longitude: -84));

        await _repository.UpsertLocationAsync(NewLocation(business.Id, "Mío", latitude: 9, longitude: -83));

        Assert.Equal("Del otro", (await _repository.GetLocationAsync(other.Id))!.Address);
        Assert.Equal("Mío", (await _repository.GetLocationAsync(business.Id))!.Address);
    }

    private async Task<BusinessEntity> CreateBusinessAsync(string slug = "bella")
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = slug, Slug = slug };
        await _repository.CreateAsync(business);
        return business;
    }

    private static BusinessLocation NewLocation(
        Guid businessId, string address, double latitude, double longitude, string? city = null) => new()
    {
        BusinessId = businessId,
        Address = address,
        City = city,
        Location = new Point(longitude, latitude) { SRID = 4326 },
    };
}
