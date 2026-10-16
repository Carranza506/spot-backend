using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using BusinessEntity = Spot.Business.Api.Models.Business;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Repositories;

/// <summary>
/// Real BusinessDbContext round-trips on EF Core's InMemory provider — see CategoryRepositoryTests
/// for why. InMemory has no transactions, row locks or partial unique indexes, so the
/// concurrency guarantees of the adds (FOR UPDATE on the parent, ux_business_photos_one_primary)
/// are verified against real Postgres instead; these tests cover the query and write logic.
/// </summary>
public sealed class PhotoRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly PhotoRepository _repository;

    public PhotoRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new PhotoRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    // ---------- Business photos: listing ----------

    [Fact]
    public async Task ListBusinessPhotosAsync_OrdersByDisplayOrderThenCreatedAtThenId_AndPagesWithinTheBusiness()
    {
        var businessId = Guid.NewGuid();
        var tieA = new Guid("00000000-0000-0000-0000-00000000000a");
        var tieB = new Guid("00000000-0000-0000-0000-00000000000b");
        await SeedBusinessPhotoAsync(businessId, "second", displayOrder: 1, T0);
        await SeedBusinessPhotoAsync(businessId, "fourth", displayOrder: 2, T0, id: tieB);
        await SeedBusinessPhotoAsync(businessId, "third", displayOrder: 2, T0, id: tieA);
        await SeedBusinessPhotoAsync(businessId, "first", displayOrder: 0, T0.AddDays(5));
        await SeedBusinessPhotoAsync(Guid.NewGuid(), "foreign", displayOrder: 0, T0);

        var (page1, total) = await _repository.ListBusinessPhotosAsync(businessId, page: 1, pageSize: 3);
        var (page2, _) = await _repository.ListBusinessPhotosAsync(businessId, page: 2, pageSize: 3);

        Assert.Equal(4, total);
        Assert.Equal(["first", "second", "third"], page1.Select(p => p.StorageKey));
        Assert.Equal(["fourth"], page2.Select(p => p.StorageKey));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(int.MaxValue)] // (page - 1) * pageSize would overflow a 32-bit offset
    public async Task ListBusinessPhotosAsync_PagePastTheLastRow_ReturnsEmptyWithTheTotal(int page)
    {
        var businessId = Guid.NewGuid();
        await SeedBusinessPhotoAsync(businessId, "only", displayOrder: 0, T0);

        var (items, total) = await _repository.ListBusinessPhotosAsync(businessId, page, pageSize: 100);

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetBusinessPhotoAsync_PhotoOfAnotherBusiness_ReturnsNull()
    {
        var photo = await SeedBusinessPhotoAsync(Guid.NewGuid(), "theirs", displayOrder: 0, T0);

        Assert.Null(await _repository.GetBusinessPhotoAsync(Guid.NewGuid(), photo.Id));
        Assert.NotNull(await _repository.GetBusinessPhotoAsync(photo.BusinessId, photo.Id));
    }

    // ---------- Business photos: writes ----------

    [Fact]
    public async Task AddBusinessPhotoAsync_Primary_UnsetsThePreviousPrimaryOfTheSameBusinessOnly()
    {
        var businessId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();
        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "old-primary", isPrimary: true), maxPhotos: 20);
        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(otherBusinessId, "other-primary", isPrimary: true), maxPhotos: 20);

        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "new-primary", isPrimary: true), maxPhotos: 20);

        await using var fresh = new BusinessDbContext(_options);
        var primaries = await fresh.BusinessPhotos.Where(p => p.IsPrimary).Select(p => p.StorageKey).OrderBy(k => k).ToListAsync();
        Assert.Equal(["new-primary", "other-primary"], primaries);
    }

    [Fact]
    public async Task AddBusinessPhotoAsync_NotPrimary_KeepsTheCurrentPrimary()
    {
        var businessId = Guid.NewGuid();
        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "primary", isPrimary: true), maxPhotos: 20);

        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "extra", isPrimary: false), maxPhotos: 20);

        await using var fresh = new BusinessDbContext(_options);
        Assert.Equal("primary", (await fresh.BusinessPhotos.SingleAsync(p => p.IsPrimary)).StorageKey);
    }

    [Fact]
    public async Task AddBusinessPhotoAsync_AtTheLimit_ThrowsAndStoresNothing_EvenForAPrimary()
    {
        var businessId = Guid.NewGuid();
        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "a", isPrimary: true), maxPhotos: 2);
        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "b", isPrimary: false), maxPhotos: 2);

        var ex = await Assert.ThrowsAsync<PhotoLimitReachedException>(() =>
            _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "c", isPrimary: true), maxPhotos: 2));

        Assert.Equal(2, ex.MaxPhotos);
        await using var fresh = new BusinessDbContext(_options);
        var rows = await fresh.BusinessPhotos.Where(p => p.BusinessId == businessId).OrderBy(p => p.StorageKey).ToListAsync();
        Assert.Equal(["a", "b"], rows.Select(p => p.StorageKey));
        // The limit is checked before touching the primary flag.
        Assert.True(rows.Single(p => p.StorageKey == "a").IsPrimary);
    }

    [Fact]
    public async Task AddBusinessPhotoAsync_LimitCountsOnlyThatBusinessesPhotos()
    {
        var businessId = Guid.NewGuid();
        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(Guid.NewGuid(), "foreign", isPrimary: false), maxPhotos: 1);

        await _repository.AddBusinessPhotoAsync(NewBusinessPhoto(businessId, "mine", isPrimary: false), maxPhotos: 1);

        Assert.Equal(1, await _db.BusinessPhotos.CountAsync(p => p.BusinessId == businessId));
    }

    [Fact]
    public async Task DeleteBusinessPhotoAsync_RemovesTheRow()
    {
        var photo = await SeedBusinessPhotoAsync(Guid.NewGuid(), "gone", displayOrder: 0, T0);
        var tracked = await _repository.GetBusinessPhotoAsync(photo.BusinessId, photo.Id);

        await _repository.DeleteBusinessPhotoAsync(tracked!);

        await using var fresh = new BusinessDbContext(_options);
        Assert.False(await fresh.BusinessPhotos.AnyAsync());
    }

    // ---------- Service photos ----------

    [Fact]
    public async Task FindServiceAsync_LoadsTheServiceWithItsBusiness()
    {
        var service = await SeedServiceAsync(businessActive: false);

        var found = await _repository.FindServiceAsync(service.Id);

        Assert.NotNull(found);
        Assert.Equal(service.BusinessId, found!.Business.Id);
        Assert.False(found.Business.IsActive);
        Assert.Null(await _repository.FindServiceAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListServicePhotosAsync_OrdersAndScopesLikeBusinessPhotos()
    {
        var serviceId = Guid.NewGuid();
        await SeedServicePhotoAsync(serviceId, "b", displayOrder: 1, T0);
        await SeedServicePhotoAsync(serviceId, "a", displayOrder: 0, T0.AddDays(1));
        await SeedServicePhotoAsync(Guid.NewGuid(), "foreign", displayOrder: 0, T0);

        var (items, total) = await _repository.ListServicePhotosAsync(serviceId, page: 1, pageSize: 20);

        Assert.Equal(2, total);
        Assert.Equal(["a", "b"], items.Select(p => p.StorageKey));
    }

    [Fact]
    public async Task ListServicePhotosAsync_HugePage_ReturnsEmpty()
    {
        var serviceId = Guid.NewGuid();
        await SeedServicePhotoAsync(serviceId, "only", displayOrder: 0, T0);

        var (items, total) = await _repository.ListServicePhotosAsync(serviceId, int.MaxValue, pageSize: 100);

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetServicePhotoAsync_PhotoOfAnotherService_ReturnsNull()
    {
        var photo = await SeedServicePhotoAsync(Guid.NewGuid(), "theirs", displayOrder: 0, T0);

        Assert.Null(await _repository.GetServicePhotoAsync(Guid.NewGuid(), photo.Id));
        Assert.NotNull(await _repository.GetServicePhotoAsync(photo.ServiceId, photo.Id));
    }

    [Fact]
    public async Task AddServicePhotoAsync_AtTheLimit_ThrowsAndStoresNothing()
    {
        var serviceId = Guid.NewGuid();
        await _repository.AddServicePhotoAsync(new ServicePhoto { ServiceId = serviceId, StorageKey = "a" }, maxPhotos: 1);

        await Assert.ThrowsAsync<PhotoLimitReachedException>(() =>
            _repository.AddServicePhotoAsync(new ServicePhoto { ServiceId = serviceId, StorageKey = "b" }, maxPhotos: 1));

        await using var fresh = new BusinessDbContext(_options);
        Assert.Equal(["a"], await fresh.ServicePhotos.Select(p => p.StorageKey).ToListAsync());
    }

    [Fact]
    public async Task DeleteServicePhotoAsync_RemovesTheRow()
    {
        var photo = await SeedServicePhotoAsync(Guid.NewGuid(), "gone", displayOrder: 0, T0);
        var tracked = await _repository.GetServicePhotoAsync(photo.ServiceId, photo.Id);

        await _repository.DeleteServicePhotoAsync(tracked!);

        await using var fresh = new BusinessDbContext(_options);
        Assert.False(await fresh.ServicePhotos.AnyAsync());
    }

    // ---------- Helpers ----------

    private static BusinessPhoto NewBusinessPhoto(Guid businessId, string key, bool isPrimary) =>
        new() { BusinessId = businessId, StorageKey = key, IsPrimary = isPrimary };

    private async Task<BusinessPhoto> SeedBusinessPhotoAsync(
        Guid businessId, string key, int displayOrder, DateTimeOffset createdAt, Guid? id = null)
    {
        var photo = new BusinessPhoto
        {
            Id = id ?? Guid.NewGuid(),
            BusinessId = businessId,
            StorageKey = key,
            DisplayOrder = displayOrder,
            CreatedAt = createdAt,
        };
        _db.BusinessPhotos.Add(photo);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return photo;
    }

    private async Task<ServicePhoto> SeedServicePhotoAsync(Guid serviceId, string key, int displayOrder, DateTimeOffset createdAt)
    {
        var photo = new ServicePhoto
        {
            Id = Guid.NewGuid(),
            ServiceId = serviceId,
            StorageKey = key,
            DisplayOrder = displayOrder,
            CreatedAt = createdAt,
        };
        _db.ServicePhotos.Add(photo);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return photo;
    }

    private async Task<ServiceEntity> SeedServiceAsync(bool businessActive)
    {
        var business = new BusinessEntity { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella", IsActive = businessActive };
        var service = new ServiceEntity { Id = Guid.NewGuid(), BusinessId = business.Id, Name = "Corte", Price = 5000, DurationMinutes = 30 };
        _db.Businesses.Add(business);
        _db.Services.Add(service);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return service;
    }
}
