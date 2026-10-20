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

    private static BookingDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
            .Options);

    private static BookingEntity NewBooking()
    {
        var start = new DateTimeOffset(2026, 9, 1, 14, 0, 0, TimeSpan.Zero);
        return new BookingEntity
        {
            UserId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ServiceId = Guid.NewGuid(),
            StartAt = start,
            EndAt = start.AddMinutes(45),
            Status = BookingStatus.PENDING,
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
