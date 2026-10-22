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

    public async Task<PaginatedResponse<BookingDto>> ListOwnAsync(
        Guid userId, BookingStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await bookingRepository.ListByUserAsync(userId, status, page, pageSize, ct);
        return ToPage(items, total, page, pageSize);
    }

    public async Task<BookingDto> GetAsync(Guid bookingId, Guid callerId, bool callerIsSuperAdmin, CancellationToken ct = default)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, ct)
                      ?? throw new BookingNotFoundException(bookingId);

        if (callerIsSuperAdmin || booking.UserId == callerId)
            return BookingDto.FromEntity(booking);

        var business = await scheduleRepository.GetBusinessAsync(booking.BusinessId, ct);
        if (business?.AccountId != callerId)
            throw new BookingAccessDeniedException();

        return BookingDto.FromEntity(booking);
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
        return ToPage(items, total, page, pageSize);
    }

    private static PaginatedResponse<BookingDto> ToPage(IReadOnlyList<BookingEntity> items, int total, int page, int pageSize) =>
        new(items.Select(BookingDto.FromEntity).ToList(),
            new PaginationMeta(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));

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
