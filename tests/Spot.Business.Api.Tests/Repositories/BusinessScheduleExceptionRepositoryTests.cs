using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Repositories;

/// <summary>
/// Real BusinessDbContext round-trips on EF Core's InMemory provider — see CategoryRepositoryTests
/// for why. InMemory doesn't enforce the (business_id, exception_date) unique index, so the
/// concurrent-insert path is simulated with an interceptor, same as BusinessRepositoryTests does
/// for favorites (#71).
/// </summary>
public sealed class BusinessScheduleExceptionRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BusinessDbContext> _options;
    private readonly BusinessDbContext _db;
    private readonly BusinessScheduleExceptionRepository _repository;

    public BusinessScheduleExceptionRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BusinessDbContext(_options);
        _repository = new BusinessScheduleExceptionRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ListByBusinessAsync_FiltersInclusiveOrdersByDateAndPagesWithinTheBusiness()
    {
        var businessId = Guid.NewGuid();
        await SeedAsync(businessId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 24));
        await SeedAsync(Guid.NewGuid(), new DateOnly(2026, 12, 24));

        var (items, total) = await _repository.ListByBusinessAsync(
            businessId, from: new DateOnly(2026, 12, 24), to: new DateOnly(2026, 12, 31), page: 1, pageSize: 2);

        Assert.Equal(3, total);
        Assert.Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25)], items.Select(e => e.ExceptionDate));
    }

    [Fact]
    public async Task CreateAsync_DateAlreadyTaken_ThrowsAndStoresNothingNew()
    {
        var businessId = Guid.NewGuid();
        await SeedAsync(businessId, new DateOnly(2026, 12, 25));

        await Assert.ThrowsAsync<ScheduleExceptionAlreadyExistsException>(() => _repository.CreateAsync(
            new BusinessScheduleException { BusinessId = businessId, ExceptionDate = new DateOnly(2026, 12, 25), IsClosed = true }));

        await using var freshDb = new BusinessDbContext(_options);
        Assert.Equal(1, await freshDb.BusinessScheduleExceptions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_DuplicateKeyFromAConcurrentInsert_ThrowsTheDomainException()
    {
        await using var racingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSaveInterceptor(PostgresErrorCodes.UniqueViolation))
            .Options);
        var exception = new BusinessScheduleException { BusinessId = Guid.NewGuid(), ExceptionDate = new DateOnly(2026, 12, 25), IsClosed = true };

        // The up-front AnyAsync sees no row, then SaveChanges loses the race (23505).
        var thrown = await Assert.ThrowsAsync<ScheduleExceptionAlreadyExistsException>(
            () => new BusinessScheduleExceptionRepository(racingDb).CreateAsync(exception));

        Assert.Equal(exception.BusinessId, thrown.BusinessId);
        Assert.Equal(exception.ExceptionDate, thrown.ExceptionDate);
        Assert.Empty(racingDb.ChangeTracker.Entries<BusinessScheduleException>());
    }

    [Fact]
    public async Task CreateAsync_OtherDatabaseError_IsNotSwallowed()
    {
        await using var failingDb = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSaveInterceptor(PostgresErrorCodes.ForeignKeyViolation))
            .Options);

        await Assert.ThrowsAsync<DbUpdateException>(() => new BusinessScheduleExceptionRepository(failingDb).CreateAsync(
            new BusinessScheduleException { BusinessId = Guid.NewGuid(), ExceptionDate = new DateOnly(2026, 12, 25), IsClosed = true }));
    }

    [Fact]
    public async Task GetByIdAsync_ExceptionOfAnotherBusiness_ReturnsNull()
    {
        var businessId = Guid.NewGuid();
        await SeedAsync(businessId, new DateOnly(2026, 12, 25));
        var id = (await _db.BusinessScheduleExceptions.SingleAsync()).Id;

        Assert.NotNull(await _repository.GetByIdAsync(businessId, id));
        Assert.Null(await _repository.GetByIdAsync(Guid.NewGuid(), id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRow()
    {
        var businessId = Guid.NewGuid();
        await SeedAsync(businessId, new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25));
        var target = await _db.BusinessScheduleExceptions.SingleAsync(e => e.ExceptionDate == new DateOnly(2026, 12, 24));

        await _repository.DeleteAsync(target);

        await using var freshDb = new BusinessDbContext(_options);
        var remaining = await freshDb.BusinessScheduleExceptions.SingleAsync();
        Assert.Equal(new DateOnly(2026, 12, 25), remaining.ExceptionDate);
    }

    private async Task SeedAsync(Guid businessId, params DateOnly[] dates)
    {
        _db.BusinessScheduleExceptions.AddRange(dates.Select(date => new BusinessScheduleException
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            ExceptionDate = date,
            IsClosed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }));
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// InMemory never raises PostgresException, so this stands in for Postgres rejecting the insert
    /// with <paramref name="sqlState"/> — e.g. 23505 when a concurrent request inserted the same date first.
    /// </summary>
    private sealed class ThrowOnSaveInterceptor(string sqlState) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database error.", new PostgresException("simulated", "ERROR", "ERROR", sqlState));
    }
}
