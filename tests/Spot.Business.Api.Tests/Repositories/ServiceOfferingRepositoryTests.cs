using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.Repositories;
using BusinessEntity = Spot.Business.Api.Models.Business;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Repositories;

/// <summary>
/// Real BusinessDbContext round-trips on EF Core's InMemory provider — see CategoryRepositoryTests
/// for why. The database-generated created_at/updated_at defaults aren't exercised here, since
/// InMemory never runs the column's DEFAULT CURRENT_TIMESTAMP.
/// </summary>
public sealed class ServiceOfferingRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly ServiceOfferingRepository _repository;

    public ServiceOfferingRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new ServiceOfferingRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_PersistsTheServiceWithAGeneratedId()
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        await new BusinessRepository(_db).CreateAsync(business);
        var service = new ServiceEntity
        {
            BusinessId = business.Id,
            Name = "Corte y peinado",
            Description = "Incluye lavado.",
            Price = 12000.00m,
            DurationMinutes = 45,
            IsActive = true,
        };

        await _repository.CreateAsync(service);

        Assert.NotEqual(Guid.Empty, service.Id);
        await using var freshContext = new BusinessDbContext(_options);
        var stored = await freshContext.Services.SingleAsync(s => s.Id == service.Id);
        Assert.Equal(business.Id, stored.BusinessId);
        Assert.Equal("Corte y peinado", stored.Name);
        Assert.Equal("Incluye lavado.", stored.Description);
        Assert.Equal(12000.00m, stored.Price);
        Assert.Equal(45, stored.DurationMinutes);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task ListAsync_FiltersByBusinessAndActive_OrdersByNameThenId()
    {
        var business = await SeedBusinessAsync("bella");
        var other = await SeedBusinessAsync("otra");
        await SeedServiceAsync(business.Id, "Corte", active: true);
        await SeedServiceAsync(business.Id, "Barba", active: true);
        await SeedServiceAsync(business.Id, "Viejo", active: false);
        await SeedServiceAsync(other.Id, "Ajeno", active: true);

        var (items, total) = await _repository.ListAsync(business.Id, page: 1, pageSize: 20, active: true);

        Assert.Equal(2, total);
        Assert.Equal(["Barba", "Corte"], items.Select(s => s.Name));
    }

    [Fact]
    public async Task ListAsync_NullActive_ReturnsBothActiveAndInactive()
    {
        var business = await SeedBusinessAsync("bella");
        await SeedServiceAsync(business.Id, "Corte", active: true);
        await SeedServiceAsync(business.Id, "Viejo", active: false);

        var (items, total) = await _repository.ListAsync(business.Id, page: 1, pageSize: 20, active: null);

        Assert.Equal(2, total);
        Assert.Equal(["Corte", "Viejo"], items.Select(s => s.Name));
    }

    [Fact]
    public async Task ListAsync_HugePage_ReturnsEmptyWithoutOverflowing()
    {
        var business = await SeedBusinessAsync("bella");
        await SeedServiceAsync(business.Id, "Corte", active: true);

        var (items, total) = await _repository.ListAsync(business.Id, page: 107374184, pageSize: 20, active: true);

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTheServiceRegardlessOfActiveState()
    {
        var business = await SeedBusinessAsync("bella");
        var inactive = await SeedServiceAsync(business.Id, "Viejo", active: false);

        var found = await _repository.GetByIdAsync(inactive.Id);

        Assert.NotNull(found);
        Assert.False(found!.IsActive);
        Assert.Null(await _repository.GetByIdAsync(Guid.NewGuid()));
    }

    private async Task<BusinessEntity> SeedBusinessAsync(string slug)
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = slug, Slug = slug };
        await new BusinessRepository(_db).CreateAsync(business);
        return business;
    }

    private async Task<ServiceEntity> SeedServiceAsync(Guid businessId, string name, bool active)
    {
        var service = new ServiceEntity
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Name = name,
            Price = 1000m,
            DurationMinutes = 30,
            IsActive = active,
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();
        return service;
    }
}
