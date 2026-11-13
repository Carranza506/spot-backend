using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Services;
using Spot.Booking.Api.Tests.Fakes;
using Spot.Shared.Exceptions;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Services;

/// <summary>BookingService's cancel / complete / no-show rules (#69), on the in-memory fakes.</summary>
public class BookingStatusTransitionTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAvailabilityRepository _schedule = new();
    private readonly FakeBookingRepository _bookings;
    private readonly FakeBookingStatusNotifier _notifier = new();
    private readonly BookingService _service;

    public BookingStatusTransitionTests()
    {
        _bookings = new FakeBookingRepository(_schedule);
        _service = new BookingService(_schedule, _bookings, new FixedTimeProvider(Now), _notifier);
    }

    // ---------- status notification (#79) ----------

    [Fact]
    public async Task Cancel_NotifiesTheNewStatus()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        await _service.CancelAsync(booking.Id, ClientId);

        Assert.Equal([(booking.Id, BookingStatus.CANCELLED)], _notifier.Notified);
    }

    [Fact]
    public async Task Cancel_WithAReason_StoresIt_BeforeNotifying()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);
        _bookings.BeforeUpdate = () => Assert.Empty(_notifier.Notified);

        var result = await _service.CancelAsync(booking.Id, ClientId, "No puedo asistir");

        Assert.Equal("No puedo asistir", result.CancelReason);
        Assert.Equal("No puedo asistir", booking.CancelReason);
        Assert.Equal([(booking.Id, BookingStatus.CANCELLED)], _notifier.Notified);
    }

    [Fact]
    public async Task Cancel_FromAnInvalidStatus_DoesNotStoreTheReason()
    {
        var booking = await SeedAsync(BookingStatus.COMPLETED);

        await Assert.ThrowsAsync<BookingRuleViolationException>(
            () => _service.CancelAsync(booking.Id, ClientId, "No puedo asistir"));

        Assert.Null(booking.CancelReason);
    }

    [Fact]
    public async Task Complete_NotifiesTheNewStatus()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        await _service.CompleteAsync(booking.Id, OwnerId);

        Assert.Equal([(booking.Id, BookingStatus.COMPLETED)], _notifier.Notified);
    }

    [Fact]
    public async Task MarkNoShow_NotifiesTheNewStatus()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        await _service.MarkNoShowAsync(booking.Id, OwnerId);

        Assert.Equal([(booking.Id, BookingStatus.NO_SHOW)], _notifier.Notified);
    }

    [Fact]
    public async Task ARejectedTransition_NotifiesNothing()
    {
        var booking = await SeedAsync(BookingStatus.COMPLETED);

        await Assert.ThrowsAsync<BookingRuleViolationException>(() => _service.CancelAsync(booking.Id, ClientId));

        Assert.Empty(_notifier.Notified);
    }

    [Fact]
    public async Task Confirm_NotifiesTheNewStatus()
    {
        var booking = await SeedAsync(BookingStatus.PENDING);

        await _service.ConfirmAsync(booking.Id, OwnerId);

        Assert.Equal([(booking.Id, BookingStatus.CONFIRMED)], _notifier.Notified);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task CompleteOrNoShow_BeforeTheStartTime_NotifiesNothing(BookingStatus to)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED, startAt: Now.AddMinutes(1));

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => Transition(to, booking.Id, OwnerId));

        Assert.Equal(BookingRuleViolationException.NotStarted, ex.Code);
        Assert.Empty(_notifier.Notified);
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

    // ---------- confirm (#146) ----------

    [Fact]
    public async Task Confirm_ByTheOwner_FromPending_Confirms()
    {
        var booking = await SeedAsync(BookingStatus.PENDING);

        var result = await _service.ConfirmAsync(booking.Id, OwnerId);

        Assert.Equal(BookingStatus.CONFIRMED, result.Status);
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task Confirm_FromAnythingButPending_Throws422AndKeepsTheStatus(BookingStatus from)
    {
        var booking = await SeedAsync(from);

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => _service.ConfirmAsync(booking.Id, OwnerId));

        Assert.Equal(BookingRuleViolationException.InvalidStatusTransition, ex.Code);
        Assert.Equal(from, booking.Status);
    }

    [Fact]
    public async Task Confirm_ByTheClient_ThrowsAccessDenied()
    {
        // Checked in the service too, not only by the BUSINESS role on the endpoint.
        var booking = await SeedAsync(BookingStatus.PENDING);

        await Assert.ThrowsAsync<BookingAccessDeniedException>(() => _service.ConfirmAsync(booking.Id, ClientId));
        Assert.Equal(BookingStatus.PENDING, booking.Status);
    }

    [Fact]
    public async Task Confirm_ByAStranger_ThrowsAccessDeniedBeforeCheckingTheStatus()
    {
        // Not PENDING would be a 422 for the owner; another business must get 403 instead.
        var booking = await SeedAsync(BookingStatus.COMPLETED);

        await Assert.ThrowsAsync<BookingAccessDeniedException>(() => _service.ConfirmAsync(booking.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Confirm_UnknownBooking_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<BookingNotFoundException>(() => _service.ConfirmAsync(Guid.NewGuid(), OwnerId));
    }

    [Fact]
    public async Task Confirm_WhenTheBusinessIsMissing_ThrowsAccessDenied()
    {
        var booking = await SeedAsync(BookingStatus.PENDING, withBusiness: false);

        await Assert.ThrowsAsync<BookingAccessDeniedException>(() => _service.ConfirmAsync(booking.Id, OwnerId));
        Assert.Equal(BookingStatus.PENDING, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)] // the client cancels while the owner confirms
    [InlineData(BookingStatus.CONFIRMED)] // two confirms at once: only one of them succeeds
    public async Task Confirm_WhenAConcurrentTransitionWinsTheRace_Throws422AndKeepsTheWinner(BookingStatus winner)
    {
        var booking = await SeedAsync(BookingStatus.PENDING);
        _bookings.BeforeUpdate = () => booking.Status = winner;

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => _service.ConfirmAsync(booking.Id, OwnerId));

        Assert.Equal(BookingRuleViolationException.InvalidStatusTransition, ex.Code);
        Assert.Equal(winner, booking.Status);
    }

    [Fact]
    public async Task Confirm_WhenTheBookingIsDeletedBeforeTheWrite_ThrowsNotFound()
    {
        var booking = await SeedAsync(BookingStatus.PENDING);
        _bookings.BeforeUpdate = () => _bookings.Created.Remove(booking);

        await Assert.ThrowsAsync<BookingNotFoundException>(() => _service.ConfirmAsync(booking.Id, OwnerId));
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

    // ---------- complete / no-show: only once the booking has started (#146) ----------

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task CompleteOrNoShow_BeforeTheStartTime_Throws422AndKeepsItConfirmed(BookingStatus to)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED, startAt: Now.AddMinutes(1));

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => Transition(to, booking.Id, OwnerId));

        Assert.Equal(BookingRuleViolationException.NotStarted, ex.Code);
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task CompleteOrNoShow_ExactlyAtTheStartTime_ChangesTheStatus(BookingStatus to)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED, startAt: Now);

        var result = await Transition(to, booking.Id, OwnerId);

        Assert.Equal(to, result.Status);
    }

    [Fact]
    public async Task Complete_FutureBookingInAWrongStatus_ReportsTheStatusNotTheTime()
    {
        var booking = await SeedAsync(BookingStatus.PENDING, startAt: Now.AddDays(1));

        var ex = await Assert.ThrowsAsync<BookingRuleViolationException>(() => _service.CompleteAsync(booking.Id, OwnerId));

        Assert.Equal(BookingRuleViolationException.InvalidStatusTransition, ex.Code);
    }

    [Fact]
    public async Task Complete_FutureBooking_ByAStranger_ThrowsAccessDeniedBeforeCheckingTheTime()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED, startAt: Now.AddDays(1));

        await Assert.ThrowsAsync<BookingAccessDeniedException>(() => _service.CompleteAsync(booking.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmAndCancel_FutureBooking_AreNotHeldBackByTheStartTime()
    {
        var toConfirm = await SeedAsync(BookingStatus.PENDING, startAt: Now.AddDays(1));
        var toCancel = await SeedAsync(BookingStatus.CONFIRMED, startAt: Now.AddDays(2));

        Assert.Equal(BookingStatus.CONFIRMED, (await _service.ConfirmAsync(toConfirm.Id, OwnerId)).Status);
        Assert.Equal(BookingStatus.CANCELLED, (await _service.CancelAsync(toCancel.Id, ClientId)).Status);
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

    private async Task<BookingEntity> SeedAsync(BookingStatus status, bool withBusiness = true, DateTimeOffset? startAt = null)
    {
        var business = new BusinessReference { Id = Guid.NewGuid(), AccountId = OwnerId, IsActive = true };
        if (withBusiness)
            _schedule.Businesses.Add(business);

        var start = startAt ?? Now.AddDays(-1);
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
