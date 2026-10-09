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
}
