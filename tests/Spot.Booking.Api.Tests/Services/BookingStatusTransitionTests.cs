using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Services;
using Spot.Booking.Api.Tests.Fakes;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Services;

/// <summary>BookingService's cancel / complete / no-show rules (#69), on the in-memory fakes.</summary>
public class BookingStatusTransitionTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly FakeAvailabilityRepository _schedule = new();
    private readonly FakeBookingRepository _bookings;
    private readonly BookingService _service;

    public BookingStatusTransitionTests()
    {
        _bookings = new FakeBookingRepository(_schedule);
        _service = new BookingService(_schedule, _bookings, TimeProvider.System);
    }

    // ---------- cancel ----------

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.CONFIRMED)]
    public async Task Cancel_ByTheClient_FromAnActiveStatus_Cancels(BookingStatus from)
    {
        var booking = await SeedAsync(from);

        var result = await _service.CancelAsync(booking.Id, ClientId);

        Assert.Equal(BookingStatus.CANCELLED, result.Status);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
    }

    [Fact]
    public async Task Cancel_ByTheBusinessOwner_Cancels()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        var result = await _service.CancelAsync(booking.Id, OwnerId);

        Assert.Equal(BookingStatus.CANCELLED, result.Status);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task Cancel_FromAFinalStatus_Throws422AndKeepsTheStatus(BookingStatus from)
    {
        var booking = await SeedAsync(from);

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => _service.CancelAsync(booking.Id, ClientId));

        Assert.Equal(BookingRuleViolationException.InvalidStatusTransition, ex.Code);
        Assert.Equal(from, booking.Status);
    }

    // ---------- complete / no-show ----------

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task CompleteOrNoShow_ByTheOwner_FromConfirmed_ChangesTheStatus(BookingStatus to)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        var result = await Transition(to, booking.Id, OwnerId);

        Assert.Equal(to, result.Status);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED, BookingStatus.PENDING)]
    [InlineData(BookingStatus.COMPLETED, BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.COMPLETED, BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW, BookingStatus.PENDING)]
    [InlineData(BookingStatus.NO_SHOW, BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW, BookingStatus.NO_SHOW)]
    public async Task CompleteOrNoShow_FromAnythingButConfirmed_Throws422(BookingStatus to, BookingStatus from)
    {
        var booking = await SeedAsync(from);

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => Transition(to, booking.Id, OwnerId));

        Assert.Equal(BookingRuleViolationException.InvalidStatusTransition, ex.Code);
        Assert.Equal(from, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task CompleteOrNoShow_ByTheClient_ThrowsAccessDenied(BookingStatus to)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        await Assert.ThrowsAsync<BookingAccessDeniedException>(() => Transition(to, booking.Id, ClientId));
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
    }

    // ---------- shared ----------

    [Fact]
    public async Task AnyTransition_ByAStranger_ThrowsAccessDeniedBeforeCheckingTheStatus()
    {
        // A final status would be a 422 for an allowed caller; a stranger must get 403 instead.
        var booking = await SeedAsync(BookingStatus.COMPLETED);

        await Assert.ThrowsAsync<BookingAccessDeniedException>(() => _service.CancelAsync(booking.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task AnyTransition_UnknownBooking_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<BookingNotFoundException>(() => _service.CancelAsync(Guid.NewGuid(), ClientId));
    }

    // ---------- business missing: fail closed ----------

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    [InlineData(BookingStatus.CANCELLED)]
    public async Task BusinessTransition_WhenTheBusinessIsMissing_ThrowsAccessDenied(BookingStatus to)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED, withBusiness: false);

        await Assert.ThrowsAsync<BookingAccessDeniedException>(() => to == BookingStatus.CANCELLED
            ? _service.CancelAsync(booking.Id, OwnerId)
            : Transition(to, booking.Id, OwnerId));
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
    }

    [Fact]
    public async Task Cancel_ByTheClient_WhenTheBusinessIsMissing_StillCancels()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED, withBusiness: false);

        var result = await _service.CancelAsync(booking.Id, ClientId);

        Assert.Equal(BookingStatus.CANCELLED, result.Status);
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task Cancel_WhenAConcurrentCompleteWinsTheRace_Throws422AndKeepsCompleted()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);
        _bookings.BeforeUpdate = () => booking.Status = BookingStatus.COMPLETED;

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => _service.CancelAsync(booking.Id, ClientId));

        Assert.Equal(BookingRuleViolationException.InvalidStatusTransition, ex.Code);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
    }

    [Fact]
    public async Task AnyTransition_WhenTheBookingIsDeletedBeforeTheWrite_ThrowsNotFound()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);
        _bookings.BeforeUpdate = () => _bookings.Created.Remove(booking);

        await Assert.ThrowsAsync<BookingNotFoundException>(() => _service.CompleteAsync(booking.Id, OwnerId));
    }

    private Task<BookingDto> Transition(BookingStatus to, Guid bookingId, Guid callerId) =>
        to == BookingStatus.COMPLETED
            ? _service.CompleteAsync(bookingId, callerId)
            : _service.MarkNoShowAsync(bookingId, callerId);

    private async Task<BookingEntity> SeedAsync(BookingStatus status, bool withBusiness = true)
    {
        var business = new BusinessReference { Id = Guid.NewGuid(), AccountId = OwnerId, IsActive = true };
        if (withBusiness)
            _schedule.Businesses.Add(business);

        var start = new DateTimeOffset(2026, 9, 1, 14, 0, 0, TimeSpan.Zero);
        var booking = new BookingEntity
        {
            UserId = ClientId,
            BusinessId = business.Id,
            ServiceId = Guid.NewGuid(),
            StartAt = start,
            EndAt = start.AddMinutes(45),
            Status = status,
            ServiceName = "Corte",
            ServicePrice = 5000m,
            ServiceDurationMinutes = 45,
        };
        await _bookings.CreateAsync(booking);
        return booking;
    }
}
