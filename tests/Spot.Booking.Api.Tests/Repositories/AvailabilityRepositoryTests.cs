using Microsoft.EntityFrameworkCore;
using Spot.Booking.Api.Data;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Repositories;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Repositories;

/// <summary>
/// Real BookingDbContext queries on EF Core's InMemory provider, same approach as
/// Spot.Business.Api.Tests' repository tests. InMemory can't check the booking_status enum
/// mapping itself (it never talks to Postgres); these tests cover the query's filters.
/// </summary>
public sealed class AvailabilityRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset RangeStart = new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEnd = new(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);

    private readonly BookingDbContext _db;
    private readonly AvailabilityRepository _repository;

    public AvailabilityRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BookingDbContext(options);
        _repository = new AvailabilityRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetActiveBookingsAsync_OnlyThisBusinessesActiveBookingsOverlappingTheHalfOpenRange()
    {
        var businessId = Guid.NewGuid();
        var seeded = await SeedBookingsAsync(
            Booking(businessId, RangeStart.AddHours(1), 1, BookingStatus.PENDING, "inside, pending"),
            Booking(businessId, RangeStart.AddHours(2), 1, BookingStatus.CONFIRMED, "inside, confirmed"),
            Booking(businessId, RangeStart.AddHours(3), 1, BookingStatus.CANCELLED, "inside, cancelled"),
            Booking(businessId, RangeStart.AddHours(4), 1, BookingStatus.COMPLETED, "inside, completed"),
            Booking(businessId, RangeStart.AddHours(5), 1, BookingStatus.NO_SHOW, "inside, no-show"),
            Booking(businessId, RangeStart.AddMinutes(-30), 1, BookingStatus.CONFIRMED, "straddles the start"),
            Booking(businessId, RangeEnd.AddMinutes(-30), 1, BookingStatus.CONFIRMED, "straddles the end"),
            Booking(businessId, RangeStart.AddHours(-1), 1, BookingStatus.CONFIRMED, "ends exactly at the start"),
            Booking(businessId, RangeEnd, 1, BookingStatus.CONFIRMED, "starts exactly at the end"),
            Booking(Guid.NewGuid(), RangeStart.AddHours(1), 1, BookingStatus.CONFIRMED, "other business"));

        var result = await _repository.GetActiveBookingsAsync(businessId, RangeStart, RangeEnd);

        // Every seeded booking of this business starts at a distinct time, so the start identifies it.
        var labelByStart = seeded.Where(b => b.BusinessId == businessId).ToDictionary(b => b.StartAt, b => b.ServiceName);
        Assert.Equal(
            ["inside, confirmed", "inside, pending", "straddles the end", "straddles the start"],
            result.Select(r => labelByStart[r.StartAt]).Order());
    }

    [Fact]
    public async Task GetScheduleExceptionsAsync_InclusiveRangeWithinTheBusiness()
    {
        var businessId = Guid.NewGuid();
        _db.BusinessScheduleExceptions.AddRange(
            Exception(businessId, new DateOnly(2026, 8, 31)),
            Exception(businessId, new DateOnly(2026, 9, 1)),
            Exception(businessId, new DateOnly(2026, 9, 30)),
            Exception(businessId, new DateOnly(2026, 10, 1)),
            Exception(Guid.NewGuid(), new DateOnly(2026, 9, 15)));
        await _db.SaveChangesAsync();

        var result = await _repository.GetScheduleExceptionsAsync(businessId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)], result.Select(e => e.ExceptionDate).Order());
    }

    [Fact]
    public async Task GetWeeklyHoursAsync_OnlyThisBusinessesRows()
    {
        var businessId = Guid.NewGuid();
        _db.BusinessHours.AddRange(
            new BusinessHoursReference { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = 1, IsClosed = true },
            new BusinessHoursReference { Id = Guid.NewGuid(), BusinessId = Guid.NewGuid(), DayOfWeek = 1, IsClosed = true });
        await _db.SaveChangesAsync();

        var result = await _repository.GetWeeklyHoursAsync(businessId);

        Assert.Equal(businessId, Assert.Single(result).BusinessId);
    }

    private async Task<BookingEntity[]> SeedBookingsAsync(params BookingEntity[] bookings)
    {
        _db.Bookings.AddRange(bookings);
        await _db.SaveChangesAsync();
        return bookings;
    }

    /// <summary>A booking of <paramref name="hours"/> hours; its ServiceName labels it for assertions.</summary>
    private static BookingEntity Booking(Guid businessId, DateTimeOffset start, int hours, BookingStatus status, string label) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        BusinessId = businessId,
        ServiceId = Guid.NewGuid(),
        StartAt = start,
        EndAt = start.AddHours(hours),
        Status = status,
        ServiceName = label,
        ServicePrice = 1000m,
        ServiceDurationMinutes = hours * 60,
    };

    private static BusinessScheduleExceptionReference Exception(Guid businessId, DateOnly date) =>
        new() { Id = Guid.NewGuid(), BusinessId = businessId, ExceptionDate = date, IsClosed = true };
}
