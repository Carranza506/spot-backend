using Spot.Booking.Api.DTOs;

namespace Spot.Booking.Api.Services;

public interface IAvailabilityService
{
    /// <summary>
    /// The business's bookable slots for each local date in [<paramref name="from"/>, <paramref name="to"/>].
    /// Slots last the service's duration when <paramref name="serviceId"/> is given, otherwise
    /// <paramref name="slotMinutes"/>. The range and slotMinutes are validated by the caller.
    /// </summary>
    /// <exception cref="BusinessNotFoundException">The business doesn't exist or is inactive.</exception>
    /// <exception cref="ServiceNotFoundException">The service doesn't exist, is inactive, or belongs to another business.</exception>
    Task<AvailabilityResponse> GetAvailabilityAsync(
        Guid businessId, DateOnly from, DateOnly to, Guid? serviceId, int slotMinutes, CancellationToken ct = default);
}
