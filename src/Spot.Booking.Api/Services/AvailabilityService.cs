using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Repositories;

namespace Spot.Booking.Api.Services;

public sealed class AvailabilityService(IAvailabilityRepository repository, TimeProvider timeProvider) : IAvailabilityService
{
    public async Task<AvailabilityResponse> GetAvailabilityAsync(
        Guid businessId, DateOnly from, DateOnly to, Guid? serviceId, int slotMinutes, CancellationToken ct = default)
    {
        var business = await repository.GetBusinessAsync(businessId, ct);
        if (business is null || !business.IsActive)
            throw new BusinessNotFoundException(businessId);

        // serviceId takes precedence over slotMinutes.
        var slotLength = slotMinutes;
        if (serviceId is { } id)
        {
            var service = await repository.GetServiceAsync(id, ct);
            if (service is null || !service.IsActive || service.BusinessId != businessId)
                throw new ServiceNotFoundException(id);

            slotLength = service.DurationMinutes;
        }

        var timeZone = BusinessTimeZone.Instance;
        var rangeStartUtc = AvailabilityCalculator.ToUtc(from, TimeOnly.MinValue, timeZone);
        var rangeEndUtc = AvailabilityCalculator.ToUtc(to.AddDays(1), TimeOnly.MinValue, timeZone);

        // Sequential on purpose: these share one DbContext, which doesn't allow concurrent queries.
        var weeklyHours = await repository.GetWeeklyHoursAsync(businessId, ct);
        var exceptions = await repository.GetScheduleExceptionsAsync(businessId, from, to, ct);
        var bookings = await repository.GetActiveBookingsAsync(
            businessId, new DateTimeOffset(rangeStartUtc), new DateTimeOffset(rangeEndUtc), ct);

        var days = AvailabilityCalculator.Calculate(
            from, to, slotLength, weeklyHours, exceptions, bookings, timeProvider.GetUtcNow(), timeZone);

        return new AvailabilityResponse(businessId, serviceId, from, to, days);
    }
}
