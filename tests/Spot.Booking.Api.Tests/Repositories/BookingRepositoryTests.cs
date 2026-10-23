using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Spot.Booking.Api.Data;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Repositories;
using Spot.Booking.Api.Services;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Repositories;

/// <summary>
/// Real BookingDbContext round-trips on EF Core's InMemory provider. InMemory never raises
/// PostgresException and can't run excl_active_booking_overlap, so the database's rejection is
/// simulated with an interceptor, same as Spot.Business.Api.Tests' repository tests.
/// </summary>
public sealed class BookingRepositoryTests
{
    [Fact]
    public async Task CreateAsync_PersistsTheBooking()
    {
        await using var db = NewContext();
        var booking = NewBooking();

        await new BookingRepository(db).CreateAsync(booking);

        Assert.Equal(booking.Id, Assert.Single(db.Bookings).Id);
    }

    [Fact]
    public async Task CreateAsync_OverlapExclusionViolation_ThrowsBookingSlotOverlapException()
    {
        await using var db = NewContext(new ThrowOnSaveInterceptor(PostgresErrorCodes.ExclusionViolation, BookingRepository.OverlapConstraint));

        var ex = await Assert.ThrowsAsync<BookingSlotOverlapException>(() => new BookingRepository(db).CreateAsync(NewBooking()));

        Assert.IsType<DbUpdateException>(ex.InnerException);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.ExclusionViolation, "some_other_constraint")]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "FK_bookings_users_user_id")]
    [InlineData(PostgresErrorCodes.NumericValueOutOfRange, null)]
    public async Task CreateAsync_AnyOtherDatabaseError_PropagatesUnchanged(string sqlState, string? constraintName)
    {
        await using var db = NewContext(new ThrowOnSaveInterceptor(sqlState, constraintName));

        await Assert.ThrowsAsync<DbUpdateException>(() => new BookingRepository(db).CreateAsync(NewBooking()));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTheBookingOrNull()
    {
        await using var db = NewContext();
        var booking = await Insert(db, NewBooking());

        var repository = new BookingRepository(db);

        Assert.Equal(booking.Id, (await repository.GetByIdAsync(booking.Id))?.Id);
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListByUserAsync_FiltersByUserAndStatus_MostRecentFirst_Paged()
    {
        await using var db = NewContext();
        var userId = Guid.NewGuid();
        var day1 = await Insert(db, NewBooking(userId: userId, startUtc: "2026-09-01T14:00:00Z"));
        var day2 = await Insert(db, NewBooking(userId: userId, startUtc: "2026-09-02T14:00:00Z"));
        var day3 = await Insert(db, NewBooking(userId: userId, startUtc: "2026-09-03T14:00:00Z", status: BookingStatus.CANCELLED));
        await Insert(db, NewBooking(startUtc: "2026-09-04T14:00:00Z"));
        var repository = new BookingRepository(db);

        var (all, total) = await repository.ListByUserAsync(userId, null, 1, 20);
        Assert.Equal([day3.Id, day2.Id, day1.Id], all.Select(b => b.Id));
        Assert.Equal(3, total);

        var (secondPage, pagedTotal) = await repository.ListByUserAsync(userId, null, 2, 2);
        Assert.Equal([day1.Id], secondPage.Select(b => b.Id));
        Assert.Equal(3, pagedTotal);

        var (pending, pendingTotal) = await repository.ListByUserAsync(userId, BookingStatus.PENDING, 1, 20);
        Assert.Equal([day2.Id, day1.Id], pending.Select(b => b.Id));
        Assert.Equal(2, pendingTotal);
    }

    [Fact]
    public async Task ListByBusinessAsync_FiltersByBusinessStatusAndHalfOpenRange_EarliestFirst()
    {
        await using var db = NewContext();
        var businessId = Guid.NewGuid();
        await Insert(db, NewBooking(businessId: businessId, startUtc: "2026-09-01T05:59:00Z"));
        var atFrom = await Insert(db, NewBooking(businessId: businessId, startUtc: "2026-09-01T06:00:00Z"));
        var inside = await Insert(db, NewBooking(businessId: businessId, startUtc: "2026-09-01T20:00:00Z", status: BookingStatus.CONFIRMED));
        await Insert(db, NewBooking(businessId: businessId, startUtc: "2026-09-02T06:00:00Z"));
        await Insert(db, NewBooking(startUtc: "2026-09-01T12:00:00Z"));
        var repository = new BookingRepository(db);
        var from = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        var (inRange, total) = await repository.ListByBusinessAsync(businessId, null, from, to, 1, 20);
        Assert.Equal([atFrom.Id, inside.Id], inRange.Select(b => b.Id));
        Assert.Equal(2, total);

        var (confirmed, _) = await repository.ListByBusinessAsync(businessId, BookingStatus.CONFIRMED, from, to, 1, 20);
        Assert.Equal([inside.Id], confirmed.Select(b => b.Id));

        var (unbounded, unboundedTotal) = await repository.ListByBusinessAsync(businessId, null, null, null, 1, 20);
        Assert.Equal(4, unboundedTotal);
        Assert.Equal(4, unbounded.Count);
    }

    [Theory]
    [InlineData(int.MaxValue, 100)] // (page - 1) * pageSize overflows int to -200
    [InlineData(int.MaxValue, 1)]
    [InlineData(1073741825, 4)] // 2^30 * 4 = 2^32 overflows int and wraps to offset 0 (the first page)
    [InlineData(3, 1)] // just past the last row
    public async Task ListAsync_PagePastTheLastRow_ReturnsNoItemsAndTheTotal(int page, int pageSize)
    {
        await using var db = NewContext();
        var userId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        await Insert(db, NewBooking(userId: userId, businessId: businessId, startUtc: "2026-09-01T14:00:00Z"));
        await Insert(db, NewBooking(userId: userId, businessId: businessId, startUtc: "2026-09-02T14:00:00Z"));
        var repository = new BookingRepository(db);

        var (own, ownTotal) = await repository.ListByUserAsync(userId, null, page, pageSize);
        var (business, businessTotal) = await repository.ListByBusinessAsync(businessId, null, null, null, page, pageSize);

        Assert.Empty(own);
        Assert.Equal(2, ownTotal);
        Assert.Empty(business);
        Assert.Equal(2, businessTotal);
    }

    private static async Task<BookingEntity> Insert(BookingDbContext db, BookingEntity booking)
    {
        await new BookingRepository(db).CreateAsync(booking);
        return booking;
    }

    private static BookingDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
            .Options);

    private static BookingEntity NewBooking(
        Guid? userId = null, Guid? businessId = null, string startUtc = "2026-09-01T14:00:00Z",
        BookingStatus status = BookingStatus.PENDING)
    {
        var start = DateTimeOffset.Parse(startUtc, System.Globalization.CultureInfo.InvariantCulture);
        return new BookingEntity
        {
            UserId = userId ?? Guid.NewGuid(),
            BusinessId = businessId ?? Guid.NewGuid(),
            ServiceId = Guid.NewGuid(),
            StartAt = start,
            EndAt = start.AddMinutes(45),
            Status = status,
            ServiceName = "Corte",
            ServicePrice = 5000m,
            ServiceDurationMinutes = 45,
        };
    }

    /// <summary>Stands in for Postgres rejecting the insert with <paramref name="sqlState"/> on <paramref name="constraintName"/>.</summary>
    private sealed class ThrowOnSaveInterceptor(string sqlState, string? constraintName) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database error.", new PostgresException(
                "simulated", "ERROR", "ERROR", sqlState, constraintName: constraintName));
    }
}
