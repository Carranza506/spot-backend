using Spot.Business.Api.DTOs;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;
using BusinessEntity = Spot.Business.Api.Models.Business;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Services;

public sealed class ServiceOfferingServiceTests
{
    private readonly FakeServiceOfferingRepository _repository = new();
    private readonly FakeBusinessRepository _businessRepository = new();
    private readonly ServiceOfferingService _service;

    public ServiceOfferingServiceTests() => _service = new ServiceOfferingService(_repository, _businessRepository);

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_Owner_CreatesAnActiveServiceUnderTheBusiness()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });

        var result = await _service.CreateAsync(business.Id, accountId, new ServiceCreateRequest
        {
            Name = "Corte y peinado",
            Description = "Incluye lavado.",
            Price = 12000.00m,
            DurationMinutes = 45,
        });

        Assert.NotNull(result);
        Assert.Equal(business.Id, result!.BusinessId);
        Assert.Equal("Corte y peinado", result.Name);
        Assert.Equal("Incluye lavado.", result.Description);
        Assert.Equal(12000.00m, result.Price);
        Assert.Equal(45, result.DurationMinutes);
        Assert.True(result.IsActive);
        Assert.Equal(result.Id, _repository.All.Single().Id);
    }

    [Fact]
    public async Task CreateAsync_TrimsNameAndNormalizesBlankDescriptionToNull()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });

        var result = await _service.CreateAsync(business.Id, accountId, new ServiceCreateRequest
        {
            Name = "  Manicure  ",
            Description = "   ",
            Price = 0m,
            DurationMinutes = 30,
        });

        Assert.Equal("Manicure", result!.Name);
        Assert.Null(result.Description);
        Assert.Equal(0m, result.Price);
    }

    [Fact]
    public async Task CreateAsync_RoundsPriceToTwoDecimals_LikeTheNumeric12_2Column()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });

        var result = await _service.CreateAsync(business.Id, accountId, new ServiceCreateRequest
        {
            Name = "Corte",
            Price = 10.555m,
            DurationMinutes = 30,
        });

        // services.price is NUMERIC(12,2): the response must match what Postgres would store.
        Assert.Equal(10.56m, result!.Price);
        Assert.Equal(10.56m, _repository.All.Single().Price);
    }

    [Fact]
    public async Task CreateAsync_InactiveBusinessOwner_StillCreates()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });

        var result = await _service.CreateAsync(business.Id, accountId, ValidRequest());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateAsync_NonOwner_ThrowsAndCreatesNothing()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.CreateAsync(business.Id, Guid.NewGuid(), ValidRequest()));

        Assert.Empty(_repository.All);
    }

    [Fact]
    public async Task CreateAsync_UnknownBusiness_ReturnsNullAndCreatesNothing()
    {
        var result = await _service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), ValidRequest());

        Assert.Null(result);
        Assert.Empty(_repository.All);
    }

    // ---------- ListAsync (owner-aware) ----------

    [Fact]
    public async Task ListAsync_Public_HidesInactiveServicesAndOrdersByName()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Barba", IsActive = true });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        var result = await _service.ListAsync(business.Id, page: 1, pageSize: 20, isActive: null, callerId: null);

        Assert.Equal(2, result!.Pagination.Total);
        Assert.Equal(["Barba", "Corte"], result.Data.Select(s => s.Name));
    }

    [Fact]
    public async Task ListAsync_Public_IsActiveFalse_StillOnlyActive()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        // Public can never see inactive services, even asking for them explicitly.
        var result = await _service.ListAsync(business.Id, page: 1, pageSize: 20, isActive: false, callerId: null);

        Assert.Equal(["Corte"], result!.Data.Select(s => s.Name));
    }

    [Fact]
    public async Task ListAsync_Owner_SeesAllServicesByDefault()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        var result = await _service.ListAsync(business.Id, page: 1, pageSize: 20, isActive: null, callerId: accountId);

        Assert.Equal(["Corte", "Viejo"], result!.Data.Select(s => s.Name));
    }

    [Fact]
    public async Task ListAsync_Owner_IsActiveFalse_ReturnsOnlyInactive()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        var result = await _service.ListAsync(business.Id, page: 1, pageSize: 20, isActive: false, callerId: accountId);

        Assert.Equal(["Viejo"], result!.Data.Select(s => s.Name));
    }

    [Fact]
    public async Task ListAsync_NonOwnerToken_TreatedAsPublic()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        // A token that isn't the owner's gets the public view.
        var result = await _service.ListAsync(business.Id, page: 1, pageSize: 20, isActive: false, callerId: Guid.NewGuid());

        Assert.Equal(["Corte"], result!.Data.Select(s => s.Name));
    }

    [Fact]
    public async Task ListAsync_UnknownBusiness_ReturnsNull() =>
        Assert.Null(await _service.ListAsync(Guid.NewGuid(), page: 1, pageSize: 20, isActive: null, callerId: null));

    [Fact]
    public async Task ListAsync_Public_InactiveBusiness_ReturnsNull()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", IsActive = false });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });

        Assert.Null(await _service.ListAsync(business.Id, page: 1, pageSize: 20, isActive: null, callerId: null));
    }

    [Fact]
    public async Task ListAsync_Owner_InactiveBusiness_CanList()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });
        _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });

        var result = await _service.ListAsync(business.Id, page: 1, pageSize: 20, isActive: null, callerId: accountId);

        Assert.Equal(["Corte"], result!.Data.Select(s => s.Name));
    }

    // ---------- GetAsync (owner-aware) ----------

    [Fact]
    public async Task GetAsync_Public_ActiveServiceOfActiveBusiness_ReturnsIt()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });

        var result = await _service.GetAsync(service.Id, callerId: null);

        Assert.Equal(service.Id, result!.Id);
    }

    [Fact]
    public async Task GetAsync_UnknownService_ReturnsNull() =>
        Assert.Null(await _service.GetAsync(Guid.NewGuid(), callerId: null));

    [Fact]
    public async Task GetAsync_Public_InactiveService_ReturnsNull()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        Assert.Null(await _service.GetAsync(service.Id, callerId: null));
    }

    [Fact]
    public async Task GetAsync_Owner_InactiveService_ReturnsIt()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        var result = await _service.GetAsync(service.Id, callerId: accountId);

        Assert.Equal(service.Id, result!.Id);
    }

    [Fact]
    public async Task GetAsync_Public_ServiceOfInactiveBusiness_ReturnsNull()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", IsActive = false });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });

        Assert.Null(await _service.GetAsync(service.Id, callerId: null));
    }

    [Fact]
    public async Task GetAsync_Owner_ServiceOfInactiveBusiness_ReturnsIt()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });

        var result = await _service.GetAsync(service.Id, callerId: accountId);

        Assert.Equal(service.Id, result!.Id);
    }

    private static ServiceCreateRequest ValidRequest() =>
        new() { Name = "Corte", Price = 5000m, DurationMinutes = 30 };
}
