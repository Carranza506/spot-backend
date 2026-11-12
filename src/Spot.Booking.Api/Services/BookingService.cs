using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Repositories;
using Spot.Shared.Pagination;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Services;

/// <summary>
/// Reads the business, service and schedule through IAvailabilityRepository (#66's read-only
/// queries) and writes through IBookingRepository.
/// </summary>
public sealed class BookingService(
    IAvailabilityRepository scheduleRepository,
    IBookingRepository bookingRepository,
    TimeProvider timeProvider,
    IBookingStatusNotifier statusNotifier) : IBookingService
{
    /// <summary>How many local calendar days after today a booking may start.</summary>
    public const int MaxDaysAhead = 60;

    public async Task<BookingDto> CreateAsync(Guid userId, BookingCreateRequest request, CancellationToken ct = default)
    {
        var businessId = request.BusinessId!.Value;
        var serviceId = request.ServiceId!.Value;

        var business = await scheduleRepository.GetBusinessAsync(businessId, ct);
        if (business is null || !business.IsActive)
            throw new BusinessNotFoundException(businessId);

        var service = await scheduleRepository.GetServiceAsync(serviceId, ct);
        if (service is null || !service.IsActive || service.BusinessId != businessId)
            throw new ServiceNotFoundException(serviceId);

        // timestamptz keeps microseconds; dropping the sub-microsecond ticks up front means the
        // response shows exactly what Postgres stores.
        var start = request.StartAt!.Value.ToUniversalTime();
        start = start.AddTicks(-(start.Ticks % 10));
        var end = start.AddMinutes(service.DurationMinutes);

        var now = timeProvider.GetUtcNow();
        if (start < now)
            throw new BookingRuleViolationException(
                BookingRuleViolationException.StartInPast, "No se puede reservar en un horario que ya pasó.");

        if (LocalDate(start).DayNumber - LocalDate(now).DayNumber > MaxDaysAhead)
            throw new BookingRuleViolationException(
                BookingRuleViolationException.TooFarAhead, $"Solo se puede reservar hasta {MaxDaysAhead} días hacia adelante.");

        await EnsureWithinOpeningHoursAsync(businessId, start, end, service.DurationMinutes, ct);

        var booking = new BookingEntity
        {
            UserId = userId,
            BusinessId = businessId,
            ServiceId = serviceId,
            StartAt = start,
            EndAt = end,
            Status = BookingStatus.PENDING,
            ServiceName = service.Name,
            ServicePrice = service.Price,
            ServiceDurationMinutes = service.DurationMinutes,
            Notes = request.Notes,
        };

        await bookingRepository.CreateAsync(booking, ct);

        // A new PENDING booking goes to the business account, so it knows there's one to confirm.
        await statusNotifier.NotifyAsync(booking);

        return await ToDtoAsync(booking, ct);
    }

    public async Task<PaginatedResponse<BookingDto>> ListOwnAsync(
        Guid userId, BookingStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await bookingRepository.ListByUserAsync(userId, status, page, pageSize, ct);
        return await ToPageAsync(items, total, page, pageSize, ct);
    }

    public async Task<BookingDto> GetAsync(Guid bookingId, Guid callerId, bool callerIsSuperAdmin, CancellationToken ct = default)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, ct)
                      ?? throw new BookingNotFoundException(bookingId);

        if (!callerIsSuperAdmin && booking.UserId != callerId
            && (await scheduleRepository.GetBusinessAsync(booking.BusinessId, ct))?.AccountId != callerId)
            throw new BookingAccessDeniedException();

        return await ToDtoAsync(booking, ct);
    }

    public async Task<PaginatedResponse<BookingDto>> ListBusinessAsync(
        Guid businessId, Guid callerId, BookingStatus? status, DateOnly? from, DateOnly? to,
        int page, int pageSize, CancellationToken ct = default)
    {
        var business = await scheduleRepository.GetBusinessAsync(businessId, ct)
                       ?? throw new BusinessNotFoundException(businessId);
        if (business.AccountId != callerId)
            throw new BookingAccessDeniedException();

        // Local calendar days → UTC instants: [start of `from`, start of the day after `to`).
        // to = 9999-12-31 has no day after it (AddDays would throw), and nothing can start later
        // anyway, so it means no upper bound.
        var timeZone = BusinessTimeZone.Instance;
        DateTimeOffset? fromUtc = from is { } f ? AvailabilityCalculator.ToUtc(f, TimeOnly.MinValue, timeZone) : null;
        DateTimeOffset? toUtcExclusive = to is { } t && t != DateOnly.MaxValue
            ? AvailabilityCalculator.ToUtc(t.AddDays(1), TimeOnly.MinValue, timeZone)
            : null;

        var (items, total) = await bookingRepository.ListByBusinessAsync(
            businessId, status, fromUtc, toUtcExclusive, page, pageSize, ct);
        return await ToPageAsync(items, total, page, pageSize, ct);
    }

    public Task<BookingDto> CancelAsync(Guid bookingId, Guid callerId, CancellationToken ct = default) =>
        ChangeStatusAsync(bookingId, callerId, clientAllowed: true, onlyOnceStarted: false,
            [BookingStatus.PENDING, BookingStatus.CONFIRMED], BookingStatus.CANCELLED,
            "Solo se puede cancelar una reservación pendiente o confirmada.", ct);

    public Task<BookingDto> ConfirmAsync(Guid bookingId, Guid callerId, CancellationToken ct = default) =>
        ChangeStatusAsync(bookingId, callerId, clientAllowed: false, onlyOnceStarted: false,
            [BookingStatus.PENDING], BookingStatus.CONFIRMED,
            "Solo se puede confirmar una reservación pendiente.", ct);

    public Task<BookingDto> CompleteAsync(Guid bookingId, Guid callerId, CancellationToken ct = default) =>
        ChangeStatusAsync(bookingId, callerId, clientAllowed: false, onlyOnceStarted: true,
            [BookingStatus.CONFIRMED], BookingStatus.COMPLETED,
            "Solo se puede completar una reservación confirmada.", ct);

    public Task<BookingDto> MarkNoShowAsync(Guid bookingId, Guid callerId, CancellationToken ct = default) =>
        ChangeStatusAsync(bookingId, callerId, clientAllowed: false, onlyOnceStarted: true,
            [BookingStatus.CONFIRMED], BookingStatus.NO_SHOW,
            "Solo se puede marcar como no presentada una reservación confirmada.", ct);

    /// <summary>
    /// 404, then 403, then 422: the status and the time are only checked once the caller may change
    /// the booking, so other users can't learn them.
    /// </summary>
    private async Task<BookingDto> ChangeStatusAsync(
        Guid bookingId, Guid callerId, bool clientAllowed, bool onlyOnceStarted, BookingStatus[] validFrom,
        BookingStatus to, string invalidMessage, CancellationToken ct)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, ct)
                      ?? throw new BookingNotFoundException(bookingId);

        var isClient = clientAllowed && booking.UserId == callerId;
        if (!isClient && (await scheduleRepository.GetBusinessAsync(booking.BusinessId, ct))?.AccountId != callerId)
            throw new BookingAccessDeniedException();

        if (!validFrom.Contains(booking.Status))
            throw new BookingRuleViolationException(BookingRuleViolationException.InvalidStatusTransition, invalidMessage);

        if (onlyOnceStarted && booking.StartAt > timeProvider.GetUtcNow())
            throw new BookingRuleViolationException(
                BookingRuleViolationException.NotStarted, "Esta acción solo se puede realizar cuando ya llegó la hora de la reservación.");

        // Lost a race with another transition, or a delete, after the read above.
        if (!await bookingRepository.UpdateStatusAsync(booking, to, ct))
            throw await bookingRepository.GetByIdAsync(bookingId, ct) is null
                ? new BookingNotFoundException(bookingId)
                : new BookingRuleViolationException(BookingRuleViolationException.InvalidStatusTransition, invalidMessage);

        await statusNotifier.NotifyAsync(booking);

        return await ToDtoAsync(booking, ct);
    }

    private async Task<BookingDto> ToDtoAsync(BookingEntity booking, CancellationToken ct)
    {
        var names = await bookingRepository.GetClientNamesAsync([booking.UserId], ct);
        return BookingDto.FromEntity(booking, names.GetValueOrDefault(booking.UserId));
    }

    /// <summary>One names query for the whole page, not one per booking.</summary>
    private async Task<PaginatedResponse<BookingDto>> ToPageAsync(
        IReadOnlyList<BookingEntity> items, int total, int page, int pageSize, CancellationToken ct)
    {
        var names = items.Count == 0
            ? new Dictionary<Guid, string>()
            : await bookingRepository.GetClientNamesAsync(items.Select(b => b.UserId).Distinct().ToList(), ct);
        return new(items.Select(b => BookingDto.FromEntity(b, names.GetValueOrDefault(b.UserId))).ToList(),
            new PaginationMeta(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, BusinessTimeZone.Instance).DateTime);

    /// <summary>
    /// The whole [start, end) must fall within the opening hours of start's local date
    /// (America/Costa_Rica), resolved exactly as availability does (<see cref="OpeningHoursResolver"/>),
    /// and start must be on the slot grid availability offers for the service: opening time plus a
    /// whole number of <paramref name="slotMinutes"/>.
    /// </summary>
    private async Task EnsureWithinOpeningHoursAsync(
        Guid businessId, DateTimeOffset start, DateTimeOffset end, int slotMinutes, CancellationToken ct)
    {
        var timeZone = BusinessTimeZone.Instance;
        var localDate = LocalDate(start);

        // Sequential on purpose: these share one DbContext, which doesn't allow concurrent queries.
        var weeklyHours = await scheduleRepository.GetWeeklyHoursAsync(businessId, ct);
        var exceptions = await scheduleRepository.GetScheduleExceptionsAsync(businessId, localDate, localDate, ct);

        var hours = OpeningHoursResolver.Resolve(
            OpeningHoursResolver.WeeklyRowFor(localDate, weeklyHours), exceptions.SingleOrDefault());

        if (hours is not { } open
            || start.UtcDateTime < AvailabilityCalculator.ToUtc(localDate, open.Open, timeZone)
            || end.UtcDateTime > AvailabilityCalculator.ToUtc(localDate, open.Close, timeZone))
            throw new BookingRuleViolationException(
                BookingRuleViolationException.OutsideBusinessHours, "El negocio está cerrado en el horario seleccionado.");

        if ((start.UtcDateTime - AvailabilityCalculator.ToUtc(localDate, open.Open, timeZone)).Ticks
            % TimeSpan.FromMinutes(slotMinutes).Ticks != 0)
            throw new BookingRuleViolationException(
                BookingRuleViolationException.NotOnSlot, "El horario seleccionado no coincide con los horarios disponibles.");
    }
}
