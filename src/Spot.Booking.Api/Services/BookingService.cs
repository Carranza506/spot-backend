using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Repositories;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Services;

/// <summary>
/// Reads the business, service and schedule through IAvailabilityRepository (#66's read-only
/// queries) and writes through IBookingRepository.
/// </summary>
public sealed class BookingService(
    IAvailabilityRepository scheduleRepository,
    IBookingRepository bookingRepository,
    TimeProvider timeProvider) : IBookingService
{
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

        if (start < timeProvider.GetUtcNow())
            throw new BookingRuleViolationException(
                BookingRuleViolationException.StartInPast, "No se puede reservar en un horario que ya pasó.");

        await EnsureWithinOpeningHoursAsync(businessId, start, end, ct);

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
        return BookingDto.FromEntity(booking);
    }

    public Task<BookingDto> CancelAsync(Guid bookingId, Guid callerId, CancellationToken ct = default) =>
        ChangeStatusAsync(bookingId, callerId, clientAllowed: true,
            [BookingStatus.PENDING, BookingStatus.CONFIRMED], BookingStatus.CANCELLED,
            "Solo se puede cancelar una reservación pendiente o confirmada.", ct);

    public Task<BookingDto> CompleteAsync(Guid bookingId, Guid callerId, CancellationToken ct = default) =>
        ChangeStatusAsync(bookingId, callerId, clientAllowed: false,
            [BookingStatus.CONFIRMED], BookingStatus.COMPLETED,
            "Solo se puede completar una reservación confirmada.", ct);

    public Task<BookingDto> MarkNoShowAsync(Guid bookingId, Guid callerId, CancellationToken ct = default) =>
        ChangeStatusAsync(bookingId, callerId, clientAllowed: false,
            [BookingStatus.CONFIRMED], BookingStatus.NO_SHOW,
            "Solo se puede marcar como no presentada una reservación confirmada.", ct);

    /// <summary>
    /// 404, then 403, then 422: the status is only checked once the caller may change the booking,
    /// so other users can't learn it.
    /// </summary>
    private async Task<BookingDto> ChangeStatusAsync(
        Guid bookingId, Guid callerId, bool clientAllowed, BookingStatus[] validFrom, BookingStatus to,
        string invalidMessage, CancellationToken ct)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, ct)
                      ?? throw new BookingNotFoundException(bookingId);

        var isClient = clientAllowed && booking.UserId == callerId;
        if (!isClient && (await scheduleRepository.GetBusinessAsync(booking.BusinessId, ct))?.AccountId != callerId)
            throw new BookingAccessDeniedException();

        if (!validFrom.Contains(booking.Status))
            throw new BookingRuleViolationException(BookingRuleViolationException.InvalidStatusTransition, invalidMessage);

        await bookingRepository.UpdateStatusAsync(booking, to, ct);
        return BookingDto.FromEntity(booking);
    }

    /// <summary>
    /// The whole [start, end) must fall within the opening hours of start's local date
    /// (America/Costa_Rica), resolved exactly as availability does (<see cref="OpeningHoursResolver"/>).
    /// It doesn't have to line up with availability's slot grid.
    /// </summary>
    private async Task EnsureWithinOpeningHoursAsync(Guid businessId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var timeZone = BusinessTimeZone.Instance;
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, timeZone).DateTime);

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
    }
}
