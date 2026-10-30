using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Repositories;

/// <summary>
/// Real BusinessDbContext round-trips on EF Core's InMemory provider. InMemory can't run
/// trg_validate_review or the unique reviews.booking_id, so their rejections are simulated with an
/// interceptor, same as BusinessRepositoryTests.
/// </summary>
public sealed class ReviewRepositoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetBookingAsync_ReturnsTheBookingOrNull()
    {
        await using var db = NewContext();
        var booking = new BookingReference { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BusinessId = Guid.NewGuid() };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var repository = new ReviewRepository(db);

        var found = await repository.GetBookingAsync(booking.Id);
        Assert.Equal((booking.UserId, booking.BusinessId), (found!.UserId, found.BusinessId));
        Assert.Null(await repository.GetBookingAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_PersistsTheReview()
    {
        await using var db = NewContext();

        await new ReviewRepository(db).CreateAsync(NewReview(Guid.NewGuid(), T0));

        Assert.Single(db.Reviews);
    }

    [Fact]
    public async Task CreateAsync_UniqueViolation_ThrowsReviewAlreadyExists()
    {
        await using var db = NewContext(new ThrowOnSaveInterceptor(PostgresErrorCodes.UniqueViolation));
        var review = NewReview(Guid.NewGuid(), T0);

        var ex = await Assert.ThrowsAsync<ReviewAlreadyExistsException>(() => new ReviewRepository(db).CreateAsync(review));

        Assert.Equal(review.BookingId, ex.BookingId);
        Assert.IsType<DbUpdateException>(ex.InnerException);
        Assert.Equal(EntityState.Detached, db.Entry(review).State);
    }

    [Fact]
    public async Task CreateAsync_TheTriggerRejectsIt_ThrowsBookingNotReviewable()
    {
        await using var db = NewContext(new ThrowOnSaveInterceptor(PostgresErrorCodes.RaiseException));
        var review = NewReview(Guid.NewGuid(), T0);

        var ex = await Assert.ThrowsAsync<BookingNotReviewableException>(() => new ReviewRepository(db).CreateAsync(review));

        Assert.Equal(review.BookingId, ex.BookingId);
        Assert.Equal(EntityState.Detached, db.Entry(review).State);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(PostgresErrorCodes.CheckViolation)]
    public async Task CreateAsync_AnyOtherDatabaseError_PropagatesUnchanged(string sqlState)
    {
        await using var db = NewContext(new ThrowOnSaveInterceptor(sqlState));

        await Assert.ThrowsAsync<DbUpdateException>(() => new ReviewRepository(db).CreateAsync(NewReview(Guid.NewGuid(), T0)));
    }

    [Fact]
    public async Task ListByBusinessAsync_OnlyThatBusiness_NewestFirst_Paged()
    {
        await using var db = NewContext();
        var businessId = Guid.NewGuid();
        var reviews = Enumerable.Range(0, 5).Select(i => NewReview(businessId, T0.AddDays(i))).ToList();
        db.Reviews.AddRange(reviews);
        db.Reviews.Add(NewReview(Guid.NewGuid(), T0.AddDays(10)));
        await db.SaveChangesAsync();

        var (items, total) = await new ReviewRepository(db).ListByBusinessAsync(businessId, page: 2, pageSize: 2);

        Assert.Equal(5, total);
        Assert.Equal([reviews[2].Id, reviews[1].Id], items.Select(r => r.Id));
    }

    [Fact]
    public async Task ListByBusinessAsync_PastTheLastPage_IsEmptyWithTheTotal()
    {
        await using var db = NewContext();
        var businessId = Guid.NewGuid();
        db.Reviews.Add(NewReview(businessId, T0));
        await db.SaveChangesAsync();

        var (items, total) = await new ReviewRepository(db).ListByBusinessAsync(businessId, page: int.MaxValue, pageSize: 100);

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    private static Review NewReview(Guid businessId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        BookingId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        BusinessId = businessId,
        Rating = 5,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };

    private static BusinessDbContext NewContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<BusinessDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new BusinessDbContext(builder.Options);
    }

    /// <summary>InMemory never raises PostgresException, so this stands in for Postgres rejecting the insert.</summary>
    private sealed class ThrowOnSaveInterceptor(string sqlState) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database error.", new PostgresException("simulated", "ERROR", "ERROR", sqlState));
    }
}
