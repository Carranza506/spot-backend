using Spot.Booking.Api.DTOs;

namespace Spot.Booking.Api.Services;

public interface IBookingService
{
    /// <summary>
    /// Creates a PENDING booking for <paramref name="userId"/> (the JWT sub). endAt is startAt plus
    /// the service's duration, and the service's name/price/duration are copied onto the booking.
    /// <paramref name="request"/> must already be validated (its required fields set).
    /// </summary>
    /// <exception cref="BusinessNotFoundException">The business doesn't exist or is inactive.</exception>
    /// <exception cref="ServiceNotFoundException">The service doesn't exist, is inactive, or belongs to another business.</exception>
    /// <exception cref="BookingRuleViolationException">startAt is in the past, or the booking isn't within the business's opening hours.</exception>
    /// <exception cref="BookingSlotOverlapException">The time range overlaps another active booking of the business.</exception>
    Task<BookingDto> CreateAsync(Guid userId, BookingCreateRequest request, CancellationToken ct = default);
}
