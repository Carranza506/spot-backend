using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Repositories;

/// <summary>
/// Real BusinessDbContext round-trips on EF Core's InMemory provider — see CategoryRepositoryTests
/// for why. The native contact_type enum mapping can only be checked against real Postgres, so
/// that's covered by the Docker e2e run instead.
/// </summary>
public sealed class BusinessContactRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly BusinessContactRepository _repository;

    public BusinessContactRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new BusinessContactRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_Primary_ClearsOtherPrimariesOfTheSameBusinessOnly()
    {
        var businessId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();
        await _repository.CreateAsync(new BusinessContact { BusinessId = businessId, Type = ContactType.PHONE, Value = "1", IsPrimary = true });
        await _repository.CreateAsync(new BusinessContact { BusinessId = otherBusinessId, Type = ContactType.PHONE, Value = "2", IsPrimary = true });

        var newPrimary = new BusinessContact { BusinessId = businessId, Type = ContactType.EMAIL, Value = "a@b.cr", IsPrimary = true };
        await _repository.CreateAsync(newPrimary);

        await using var fresh = new BusinessDbContext(_options);
        var primaries = await fresh.BusinessContacts.Where(c => c.IsPrimary).ToListAsync();
        Assert.Equal(2, primaries.Count);
        Assert.Contains(primaries, c => c.Id == newPrimary.Id);
        Assert.Contains(primaries, c => c.BusinessId == otherBusinessId);
    }

    [Fact]
    public async Task ListByBusinessAsync_OrdersPrimaryFirstAndPagesWithinTheBusiness()
    {
        var businessId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;
        await SeedAsync(new BusinessContact { BusinessId = businessId, Type = ContactType.PHONE, Value = "old", CreatedAt = t0 });
        await SeedAsync(new BusinessContact { BusinessId = businessId, Type = ContactType.PHONE, Value = "new", CreatedAt = t0.AddMinutes(1) });
        await SeedAsync(new BusinessContact { BusinessId = businessId, Type = ContactType.PHONE, Value = "primary", IsPrimary = true, CreatedAt = t0.AddMinutes(2) });
        await SeedAsync(new BusinessContact { BusinessId = Guid.NewGuid(), Type = ContactType.PHONE, Value = "foreign", CreatedAt = t0 });

        var (page1, total) = await _repository.ListByBusinessAsync(businessId, page: 1, pageSize: 2);
        var (page2, _) = await _repository.ListByBusinessAsync(businessId, page: 2, pageSize: 2);

        Assert.Equal(3, total);
        Assert.Equal(["primary", "old"], page1.Select(c => c.Value));
        Assert.Equal(["new"], page2.Select(c => c.Value));
    }

    [Fact]
    public async Task GetByIdAsync_ContactOfAnotherBusiness_ReturnsNull()
    {
        var contact = await SeedAsync(new BusinessContact { BusinessId = Guid.NewGuid(), Type = ContactType.PHONE, Value = "1" });

        Assert.Null(await _repository.GetByIdAsync(Guid.NewGuid(), contact.Id));
        Assert.NotNull(await _repository.GetByIdAsync(contact.BusinessId, contact.Id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRowPermanently()
    {
        var contact = await SeedAsync(new BusinessContact { BusinessId = Guid.NewGuid(), Type = ContactType.PHONE, Value = "1" });
        var tracked = await _repository.GetByIdAsync(contact.BusinessId, contact.Id);

        await _repository.DeleteAsync(tracked!);

        await using var fresh = new BusinessDbContext(_options);
        Assert.False(await fresh.BusinessContacts.AnyAsync(c => c.Id == contact.Id));
    }

    private async Task<BusinessContact> SeedAsync(BusinessContact contact)
    {
        await using var seedContext = new BusinessDbContext(_options);
        seedContext.BusinessContacts.Add(contact);
        await seedContext.SaveChangesAsync();
        return contact;
    }
}
