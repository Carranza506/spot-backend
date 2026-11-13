using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
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

    private readonly FakeBillingRepository _billingRepository = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero));

    public ServiceOfferingServiceTests() =>
        _service = new ServiceOfferingService(_repository, _businessRepository, _billingRepository, _clock);

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

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { Name = Optional<string>.Of("  Corte Premium  "), Price = Optional<decimal?>.Of(9000m) });

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

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { IsActive = Optional<bool?>.Of(true) });

        Assert.True(result!.IsActive);
    }

    // ---------- Plan limit (FREE: 5 active services) ----------

    private const int FreeMaxServices = 5;

    private static ServiceCreateRequest NewService(string name = "Nuevo") =>
        new() { Name = name, Price = 5000m, DurationMinutes = 30 };

    private ServiceEntity SeedService(Guid businessId, bool active, string name = "Servicio") =>
        _repository.Seed(new ServiceEntity { BusinessId = businessId, Name = name, Price = 5000m, DurationMinutes = 30, IsActive = active });

    private void SeedActiveServices(Guid businessId, int count)
    {
        for (var i = 0; i < count; i++)
            SeedService(businessId, active: true, name: $"Servicio {i}");
    }

    private void SeedProSubscription(Guid accountId, DateTimeOffset periodEnd) =>
        _billingRepository.Seed(new Subscription
        {
            AccountId = accountId,
            PlanId = _billingRepository.ProPlan.Id,
            Status = SubscriptionStatus.ACTIVE,
            StripeCustomerId = "cus_test",
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
            CurrentPeriodEnd = periodEnd,
        });

    [Fact]
    public async Task CreateAsync_FreePlan_AtTheLimit_ThrowsAndCreatesNothing()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        SeedActiveServices(business.Id, FreeMaxServices);

        var ex = await Assert.ThrowsAsync<ServiceLimitReachedException>(() => _service.CreateAsync(business.Id, accountId, NewService()));

        Assert.Equal(FreeMaxServices, ex.MaxServices);
        Assert.Equal(FreeMaxServices, _repository.All.Count);
    }

    [Fact]
    public async Task CreateAsync_FreePlan_OnlyCountsThisBusinessesActiveServices()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var other = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otra" });
        SeedActiveServices(business.Id, FreeMaxServices - 1);
        SeedService(business.Id, active: false);
        SeedActiveServices(other.Id, FreeMaxServices);

        var result = await _service.CreateAsync(business.Id, accountId, NewService());

        Assert.NotNull(result);
        Assert.Equal(FreeMaxServices, _repository.All.Count(s => s.BusinessId == business.Id && s.IsActive));
    }

    [Fact]
    public async Task CreateAsync_ProPlanInEffect_HasNoLimit()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        SeedActiveServices(business.Id, FreeMaxServices);
        SeedProSubscription(accountId, periodEnd: _clock.Now.AddDays(10));

        var result = await _service.CreateAsync(business.Id, accountId, NewService());

        Assert.NotNull(result);
        Assert.Equal(FreeMaxServices + 1, _repository.All.Count);
    }

    [Fact]
    public async Task CreateAsync_ProPeriodOver_CountsAsFree()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        SeedActiveServices(business.Id, FreeMaxServices);
        SeedProSubscription(accountId, periodEnd: _clock.Now.AddMinutes(-1));

        await Assert.ThrowsAsync<ServiceLimitReachedException>(() => _service.CreateAsync(business.Id, accountId, NewService()));
    }

    [Fact]
    public async Task UpdateAsync_ReactivatingAtTheLimit_Throws()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        SeedActiveServices(business.Id, FreeMaxServices);
        var inactive = SeedService(business.Id, active: false);

        await Assert.ThrowsAsync<ServiceLimitReachedException>(() =>
            _service.UpdateAsync(inactive.Id, accountId, new ServiceUpdateRequest { IsActive = Optional<bool?>.Of(true) }));
    }

    [Fact]
    public async Task UpdateAsync_ReactivatingBelowTheLimit_Reactivates()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        SeedActiveServices(business.Id, FreeMaxServices - 1);
        var inactive = SeedService(business.Id, active: false);

        var result = await _service.UpdateAsync(inactive.Id, accountId, new ServiceUpdateRequest { IsActive = Optional<bool?>.Of(true) });

        Assert.True(result!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_AtTheLimit_EditingAnActiveServiceOrSendingIsActiveTrueAgain_StillSaves()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        SeedActiveServices(business.Id, FreeMaxServices - 1);
        var active = SeedService(business.Id, active: true, name: "Corte");

        var result = await _service.UpdateAsync(active.Id, accountId, new ServiceUpdateRequest
        {
            Name = Optional<string?>.Of("Corte clásico"),
            IsActive = Optional<bool?>.Of(true),
        });

        Assert.Equal("Corte clásico", result!.Name);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_AtTheLimit_DeactivatingIsAllowed()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        SeedActiveServices(business.Id, FreeMaxServices);
        var service = _repository.All.First();

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { IsActive = Optional<bool?>.Of(false) });

        Assert.False(result!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_RoundsPriceToTwoDecimals()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { Price = Optional<decimal?>.Of(10.555m) });

        Assert.Equal(10.56m, result!.Price);
    }

    [Fact]
    public async Task UpdateAsync_OwnerOfInactiveBusiness_StillUpdates()
    {
        var accountId = Guid.NewGuid();
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        var result = await _service.UpdateAsync(service.Id, accountId, new ServiceUpdateRequest { Name = Optional<string>.Of("Nuevo") });

        Assert.Equal("Nuevo", result!.Name);
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_ThrowsAndDoesNotChange()
    {
        var business = _businessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var service = _repository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 5000m, DurationMinutes = 30, IsActive = true });

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.UpdateAsync(service.Id, Guid.NewGuid(), new ServiceUpdateRequest { Name = Optional<string>.Of("Hackeado") }));

        Assert.Equal("Corte", _repository.All.Single().Name);
    }

    [Fact]
    public async Task UpdateAsync_UnknownService_ReturnsNull()
    {
        var result = await _service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new ServiceUpdateRequest { Name = Optional<string>.Of("X") });

        Assert.Null(result);
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

    [Fact]
    public async Task DeactivateAsync_BusinessMissing_ReturnsFalse()
    {
        // Service exists but its business was never seeded: GetOwnedAsync returns null, so the
        // fail-closed check returns false (404) instead of deactivating.
        var service = _repository.Seed(new ServiceEntity { BusinessId = Guid.NewGuid(), Name = "Huerfano", Price = 5000m, DurationMinutes = 30, IsActive = true });

        var result = await _service.DeactivateAsync(service.Id, Guid.NewGuid());

        Assert.False(result);
        Assert.True(_repository.All.Single().IsActive);  // unchanged
    }

    private static ServiceCreateRequest ValidRequest() =>
        new() { Name = "Corte", Price = 5000m, DurationMinutes = 30 };
}
