using Spot.Business.Api.DTOs;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Services;

public sealed class ServiceOfferingServiceTests
{
    private readonly FakeServiceOfferingRepository _repository = new();
    private readonly FakeBusinessRepository _businessRepository = new();
    private readonly ServiceOfferingService _service;

    public ServiceOfferingServiceTests() => _service = new ServiceOfferingService(
        _repository, new BusinessService(_businessRepository, new FakeCategoryRepository()));

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

    private static ServiceCreateRequest ValidRequest() =>
        new() { Name = "Corte", Price = 5000m, DurationMinutes = 30 };
}
