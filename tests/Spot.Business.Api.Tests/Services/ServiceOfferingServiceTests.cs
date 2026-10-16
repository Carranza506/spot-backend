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

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_Owner_AppliesOnlyTheProvidedFields()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity
        {
            BusinessId = business.Id, Name = "Corte", Description = "vieja", Price = 5000m, DurationMinutes = 30, IsActive = true,
        });

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { Name = "  Corte Premium  ", Price = 9000m });

        Assert.NotNull(result);
        Assert.Equal("Corte Premium", result!.Name);   // trimmed
        Assert.Equal(9000m, result.Price);
        Assert.Equal("vieja", result.Description);      // untouched (omitted)
        Assert.Equal(30, result.DurationMinutes);       // untouched
    }

    [Fact]
    public async Task UpdateAsync_ExplicitNullDescription_ClearsIt()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Description = "tenia", Price = 5000m, DurationMinutes = 30, IsActive = true });

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { Description = Optional<string?>.Of(null) });

        Assert.Null(result!.Description);
    }

    [Fact]
    public async Task UpdateAsync_IsActiveTrue_ReactivatesTheService()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = false });

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { IsActive = true });

        Assert.True(result!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_RoundsPriceToTwoDecimals()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { Price = 10.555m });

        Assert.Equal(10.56m, result!.Price);
    }

    [Fact]
    public async Task UpdateAsync_OwnerOfInactiveBusiness_StillUpdates()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { Name = "Nuevo" });

        Assert.Equal("Nuevo", result!.Name);
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_ThrowsAndDoesNotChange()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.UpdateAsync(service.Id, Guid.NewGuid(), new ServiceUpdateRequest { Name = "Hackeado" }));

        Assert.Equal("Corte", _repository.All.Single().Name);
    }

    [Fact]
    public async Task UpdateAsync_UnknownService_ReturnsNull()
    {
        var result = await _service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new ServiceUpdateRequest { Name = "X" });

        Assert.Null(result);
    }

    // ---------- DeactivateAsync ----------

    [Fact]
    public async Task DeactivateAsync_Owner_SoftDeletesTheService()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        var result = await _service.DeactivateAsync(service.Id, accountId);

        Assert.True(result);
        Assert.False(_repository.All.Single().IsActive);  // flipped, not removed
        Assert.Single(_repository.All);                   // the row still exists
    }

    [Fact]
    public async Task DeactivateAsync_AlreadyInactive_IsIdempotent()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", Price = 5000m, DurationMinutes = 30, IsActive = false });

        var result = await _service.DeactivateAsync(service.Id, accountId);

        Assert.True(result);
        Assert.False(_repository.All.Single().IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_NonOwner_ThrowsAndKeepsItActive()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.DeactivateAsync(service.Id, Guid.NewGuid()));

        Assert.True(_repository.All.Single().IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_UnknownService_ReturnsFalse() =>
        Assert.False(await _service.DeactivateAsync(Guid.NewGuid(), Guid.NewGuid()));

    private static ServiceCreateRequest ValidRequest() =>
        new() { Name = "Corte", Price = 5000m, DurationMinutes = 30 };
}
