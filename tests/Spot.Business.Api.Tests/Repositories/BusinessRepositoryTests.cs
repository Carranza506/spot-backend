using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
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
/// covered by the Docker e2e run instead. UpsertLocationAsync's retry on a duplicate business_id
/// is simulated with an interceptor (see the location tests below).
/// </summary>
public sealed class BusinessRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly BusinessRepository _repository;
    private readonly CategoryRepository _categoryRepository;

    public BusinessRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new BusinessRepository(_db);
        _categoryRepository = new CategoryRepository(_db);
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
        var service = new BusinessService(_repository, _categoryRepository);

        var deactivated = await service.DeactivateAsync(business.Id, accountId);

        Assert.True(deactivated);
        await using var freshContext = new BusinessDbContext(_options);
        var stored = await freshContext.Businesses.SingleAsync(b => b.Id == business.Id);
        Assert.False(stored.IsActive);
    }

    // ---------- ListCategoriesAsync ----------

    [Fact]
    public async Task ListCategoriesAsync_ReturnsAssignedCategoriesOrderedByNameAndPaged()
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        await _repository.CreateAsync(business);
        var zoo = await SeedCategoryAsync("Zoologia");
        var arte = await SeedCategoryAsync("Arte");
        var otherBusiness = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro", Slug = "otro" };
        await _repository.CreateAsync(otherBusiness);
        var ajena = await SeedCategoryAsync("Ajena");
        await LinkAsync(business.Id, zoo.Id);
        await LinkAsync(business.Id, arte.Id);
        await LinkAsync(otherBusiness.Id, ajena.Id);

        var (items, total) = await _repository.ListCategoriesAsync(business.Id, page: 1, pageSize: 20);

        Assert.Equal(2, total);
        Assert.Equal(["Arte", "Zoologia"], items.Select(c => c.Name));
    }

    [Fact]
    public async Task ListCategoriesAsync_BusinessWithoutCategories_ReturnsEmpty()
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        await _repository.CreateAsync(business);

        var (items, total) = await _repository.ListCategoriesAsync(business.Id, page: 1, pageSize: 20);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    // ---------- ReplaceCategoriesAsync ----------

    [Fact]
    public async Task ReplaceCategoriesAsync_RemovesOldRowsAndInsertsTheNewSet()
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        await _repository.CreateAsync(business);
        var vieja = await SeedCategoryAsync("Vieja");
        var nueva = await SeedCategoryAsync("Nueva");
        await LinkAsync(business.Id, vieja.Id);

        var result = await _repository.ReplaceCategoriesAsync(business.Id, [nueva.Id]);

        Assert.Equal(["Nueva"], result.Select(c => c.Name));
        await using var freshDb = new BusinessDbContext(_options);
        var rows = await freshDb.BusinessCategories.Where(bc => bc.BusinessId == business.Id).ToListAsync();
        Assert.Equal([nueva.Id], rows.Select(bc => bc.CategoryId));
    }

    // ---------- SearchAsync (no `q`: EF.Functions.ILike isn't translated by the InMemory provider,
    // so the q/ILIKE path is covered in BusinessServiceTests via the fake instead) ----------

    [Fact]
    public async Task SearchAsync_FiltersByCategory()
    {
        var bella = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" };
        var otro = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro", Slug = "otro" };
        await _repository.CreateAsync(bella);
        await _repository.CreateAsync(otro);
        var belleza = await SeedCategoryAsync("Belleza");
        await LinkAsync(bella.Id, belleza.Id);

        var (items, total) = await _repository.SearchAsync(null, belleza.Id, null, null, null, null, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal(["Bella"], items.Select(b => b.Name));
    }

    [Fact]
    public async Task SearchAsync_FiltersByCity_ExcludesBusinessWithoutLocation()
    {
        var conUbicacion = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Con ubicación", Slug = "con" };
        var sinUbicacion = new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Sin ubicación", Slug = "sin" };
        await _repository.CreateAsync(conUbicacion);
        await _repository.CreateAsync(sinUbicacion);
        await SeedLocationAsync(conUbicacion.Id, city: "San José", province: "San José");

        var (items, total) = await _repository.SearchAsync(null, null, "San José", null, null, null, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal(["Con ubicación"], items.Select(b => b.Name));
    }

    [Fact]
    public async Task SearchAsync_NoFilters_IncludesBusinessWithoutLocationButExcludesInactive()
    {
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Activo", Slug = "activo" });
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Inactivo", Slug = "inactivo", IsActive = false });

        var (items, total) = await _repository.SearchAsync(null, null, null, null, null, null, page: 1, pageSize: 20);

        Assert.Equal(1, total);
        Assert.Equal(["Activo"], items.Select(b => b.Name));
    }

    [Fact]
    public async Task SearchAsync_SameName_PaginatesDeterministicallyByIdTieBreak()
    {
        // Same name on purpose: without the id tie-break, paging could repeat or skip a row.
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella-1" });
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella-2" });
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella-3" });

        var (page1, total) = await _repository.SearchAsync(null, null, null, null, null, null, page: 1, pageSize: 2);
        var (page2, _) = await _repository.SearchAsync(null, null, null, null, null, null, page: 2, pageSize: 2);

        Assert.Equal(3, total);
        // No row repeated across pages and none skipped: the three ids together are distinct.
        var ids = page1.Concat(page2).Select(b => b.Id).ToList();
        Assert.Equal(3, ids.Count);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public async Task SearchAsync_HugePage_ReturnsEmptyWithoutOverflowing()
    {
        await _repository.CreateAsync(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", Slug = "bella" });

        // (page-1)*pageSize would overflow int into a negative OFFSET and 500 — must not here.
        var (items, total) = await _repository.SearchAsync(null, null, null, null, null, null, page: 107374184, pageSize: 20);

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    [Theory]
    [InlineData("50%", "50\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("plain", "plain")]
    public void EscapeLikePattern_EscapesWildcardsAndEscapeChar(string input, string expected)
    {
        Assert.Equal(expected, BusinessRepository.EscapeLikePattern(input));
    }

    private async Task SeedLocationAsync(Guid businessId, string? city = null, string? province = null)
    {
        _db.BusinessLocations.Add(new BusinessLocation
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Address = "Calle 1",
            City = city,
            Province = province,
            Location = new Point(0, 0) { SRID = 4326 },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    // ---------- Favorites ----------

    [Fact]
    public async Task ListFavoritesAsync_FiltersInactiveBeforePagingAndOrdersNewestFirst()
    {
        var userId = Guid.NewGuid();
        var oldest = await CreateBusinessAsync("oldest");
        var inactive = await CreateBusinessAsync("inactive", isActive: false);
        var newest = await CreateBusinessAsync("newest");
        var someoneElses = await CreateBusinessAsync("someone-elses");
        var t0 = DateTimeOffset.UtcNow;
        await SeedFavoriteAsync(userId, oldest.Id, t0);
        await SeedFavoriteAsync(userId, inactive.Id, t0.AddDays(1));
        await SeedFavoriteAsync(userId, newest.Id, t0.AddDays(2));
        await SeedFavoriteAsync(Guid.NewGuid(), someoneElses.Id, t0.AddDays(3));

        var (items, total) = await _repository.ListFavoritesAsync(userId, page: 1, pageSize: 1);

        Assert.Equal(2, total);
        var favorite = Assert.Single(items);
        Assert.Equal(newest.Id, favorite.BusinessId);
        Assert.Equal("newest", favorite.Business.Name);
    }

    [Fact]
    public async Task AddFavoriteAsync_AlreadyAFavorite_KeepsOneRowAndTheOriginalCreatedAt()
    {
        var userId = Guid.NewGuid();
        var business = await CreateBusinessAsync("bella");
        var original = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await SeedFavoriteAsync(userId, business.Id, original);

        await _repository.AddFavoriteAsync(userId, business.Id);

        await using var freshDb = new BusinessDbContext(_options);
        var row = await freshDb.FavoriteBusinesses.SingleAsync(f => f.UserId == userId);
        Assert.Equal(original, row.CreatedAt);
    }

    [Fact]
    public async Task AddFavoriteAsync_DuplicateKeyFromAConcurrentInsert_IsTreatedAsSuccess()
    {
        await using var racingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSaveInterceptor(PostgresErrorCodes.UniqueViolation))
            .Options);

        // The up-front AnyAsync sees no row, then SaveChanges loses the race (23505): no exception.
        await new BusinessRepository(racingDb).AddFavoriteAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(racingDb.ChangeTracker.Entries<FavoriteBusiness>());
    }

    [Fact]
    public async Task AddFavoriteAsync_OtherDatabaseError_IsNotSwallowed()
    {
        await using var failingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSaveInterceptor(PostgresErrorCodes.ForeignKeyViolation))
            .Options);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => new BusinessRepository(failingDb).AddFavoriteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveFavoriteAsync_DeletesOnlyTheCallersRow()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var business = await CreateBusinessAsync("bella");
        await SeedFavoriteAsync(userId, business.Id, DateTimeOffset.UtcNow);
        await SeedFavoriteAsync(otherUserId, business.Id, DateTimeOffset.UtcNow);

        await _repository.RemoveFavoriteAsync(userId, business.Id);
        await _repository.RemoveFavoriteAsync(userId, Guid.NewGuid());

        await using var freshDb = new BusinessDbContext(_options);
        var rows = await freshDb.FavoriteBusinesses.ToListAsync();
        Assert.Equal([otherUserId], rows.Select(f => f.UserId));
    }

    // ---------- ListHoursAsync / ReplaceHoursAsync ----------

    [Fact]
    public async Task ListHoursAsync_OrdersByDayOfWeekAndPagesWithinTheBusiness()
    {
        var business = await CreateBusinessAsync("bella");
        var other = await CreateBusinessAsync("otra");
        await SeedHoursAsync(business.Id, 4, 0, 2);
        await SeedHoursAsync(other.Id, 1);

        var (items, total) = await _repository.ListHoursAsync(business.Id, page: 1, pageSize: 2);

        Assert.Equal(3, total);
        Assert.Equal([0, 2], items.Select(h => (int)h.DayOfWeek));
    }

    [Fact]
    public async Task ReplaceHoursAsync_UpdatesInPlaceInsertsNewDaysAndDeletesMissingOnes()
    {
        var business = await CreateBusinessAsync("bella");
        var other = await CreateBusinessAsync("otra");
        await SeedHoursAsync(business.Id, 1, 2);
        await SeedHoursAsync(other.Id, 2);
        var mondayId = (await _db.BusinessHours.SingleAsync(h => h.BusinessId == business.Id && h.DayOfWeek == 1)).Id;

        var result = await _repository.ReplaceHoursAsync(business.Id,
        [
            new BusinessHours { DayOfWeek = 5, IsClosed = true },
            new BusinessHours { DayOfWeek = 1, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(12, 0) },
        ]);

        Assert.Equal([1, 5], result.Select(h => (int)h.DayOfWeek));
        await using var freshDb = new BusinessDbContext(_options);
        var rows = await freshDb.BusinessHours.Where(h => h.BusinessId == business.Id).OrderBy(h => h.DayOfWeek).ToListAsync();
        Assert.Equal([1, 5], rows.Select(h => (int)h.DayOfWeek));
        Assert.Equal(mondayId, rows[0].Id);
        Assert.Equal(new TimeOnly(8, 0), rows[0].OpenTime);
        Assert.Equal(1, await freshDb.BusinessHours.CountAsync(h => h.BusinessId == other.Id));
    }

    private async Task<BusinessEntity> CreateBusinessAsync(string slug = "bella", bool isActive = true)
    {
        var business = new BusinessEntity { AccountId = Guid.NewGuid(), Name = slug, Slug = slug, IsActive = isActive };
        await _repository.CreateAsync(business);
        return business;
    }

    private async Task SeedFavoriteAsync(Guid userId, Guid businessId, DateTimeOffset createdAt)
    {
        _db.FavoriteBusinesses.Add(new FavoriteBusiness { UserId = userId, BusinessId = businessId, CreatedAt = createdAt });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// InMemory never raises PostgresException, so this stands in for Postgres rejecting the insert
    /// with <paramref name="sqlState"/> — e.g. 23505 when a concurrent request inserted the same favorite first.
    /// </summary>
    private sealed class ThrowOnSaveInterceptor(string sqlState) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database error.", new PostgresException("simulated", "ERROR", "ERROR", sqlState));
    }

    private async Task SeedHoursAsync(Guid businessId, params int[] days)
    {
        _db.BusinessHours.AddRange(days.Select(day => new BusinessHours
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            DayOfWeek = (short)day,
            IsClosed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }));
        await _db.SaveChangesAsync();
    }

    private async Task<Category> SeedCategoryAsync(string name)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    private async Task LinkAsync(Guid businessId, Guid categoryId)
    {
        _db.BusinessCategories.Add(new BusinessCategory { BusinessId = businessId, CategoryId = categoryId, CreatedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();
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
    public async Task UpsertLocationAsync_RowExists_ReplacesItKeepingIdCreatedAtAndASingleRow()
    {
        var business = await CreateBusinessAsync();
        // InMemory has no created_at default, so the first row carries an explicit one to check it survives the replace.
        var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var firstLocation = NewLocation(business.Id, "Viejo local", latitude: 9.9325, longitude: -84.0795, city: "San José");
        firstLocation.CreatedAt = createdAt;
        var first = await _repository.UpsertLocationAsync(firstLocation);
        var firstId = first.Id;
        _db.ChangeTracker.Clear();

        var second = await _repository.UpsertLocationAsync(NewLocation(business.Id, "Nuevo local", latitude: 9.9981, longitude: -84.1165));

        Assert.Equal(firstId, second.Id);
        Assert.Equal(createdAt, second.CreatedAt);
        await using var freshContext = new BusinessDbContext(_options);
        var stored = Assert.Single(await freshContext.BusinessLocations.Where(l => l.BusinessId == business.Id).ToListAsync());
        Assert.Equal("Nuevo local", stored.Address);
        Assert.Null(stored.City);
        Assert.Equal(9.9981, stored.Location.Y);
        Assert.Equal(createdAt, stored.CreatedAt);
    }

    [Fact]
    public async Task UpsertLocationAsync_DuplicateKeyFromAConcurrentFirstPut_RetriesAsAnUpdateOfThatRow()
    {
        var databaseName = Guid.NewGuid().ToString();
        var businessId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var concurrent = NewLocation(businessId, "Del PUT concurrente", latitude: 10, longitude: -84);
        concurrent.CreatedAt = createdAt;
        await using var racingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new ConcurrentLocationInsertInterceptor(databaseName, concurrent))
            .Options);

        // The up-front lookup sees no row, then the insert loses the race (23505) and is retried as an update.
        var stored = await new BusinessRepository(racingDb).UpsertLocationAsync(
            NewLocation(businessId, "Mío", latitude: 9.9325, longitude: -84.0795));

        Assert.Equal(concurrent.Id, stored.Id);
        Assert.Equal(createdAt, stored.CreatedAt);
        await using var freshDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
        var row = Assert.Single(await freshDb.BusinessLocations.Where(l => l.BusinessId == businessId).ToListAsync());
        Assert.Equal(concurrent.Id, row.Id);
        Assert.Equal("Mío", row.Address);
        Assert.Equal(9.9325, row.Location.Y);
    }

    [Fact]
    public async Task UpsertLocationAsync_OtherDatabaseError_IsRethrown()
    {
        await using var failingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSaveInterceptor(PostgresErrorCodes.ForeignKeyViolation))
            .Options);

        await Assert.ThrowsAsync<DbUpdateException>(() => new BusinessRepository(failingDb).UpsertLocationAsync(
            NewLocation(Guid.NewGuid(), "Mío", latitude: 9.9325, longitude: -84.0795)));
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

    /// <summary>
    /// Stands in for a concurrent first PUT that wins the race: on the first save it inserts
    /// <paramref name="concurrent"/> through another context and then fails the caller's insert
    /// with 23505, as Postgres would on the unique business_id. Later saves go through.
    /// </summary>
    private sealed class ConcurrentLocationInsertInterceptor(string databaseName, BusinessLocation concurrent)
        : SaveChangesInterceptor
    {
        private bool _raced;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_raced)
                return result;

            _raced = true;
            await using var otherRequestDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options);
            otherRequestDb.BusinessLocations.Add(concurrent);
            await otherRequestDb.SaveChangesAsync(cancellationToken);

            throw new DbUpdateException("Simulated duplicate key.", new PostgresException(
                "duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
                constraintName: "IX_business_locations_business_id"));
        }
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
