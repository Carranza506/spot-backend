using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Models;
using Spot.Shared.Pagination;

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

    /// <summary>PENDING or CONFIRMED → CANCELLED, by the booking's client or the owner of its business.</summary>
    /// <exception cref="BookingNotFoundException">The booking doesn't exist.</exception>
    /// <exception cref="BookingAccessDeniedException"><paramref name="callerId"/> is neither the client nor the owner.</exception>
    /// <exception cref="BookingRuleViolationException">The booking isn't PENDING or CONFIRMED.</exception>
    Task<BookingDto> CancelAsync(Guid bookingId, Guid callerId, CancellationToken ct = default);

    /// <summary>CONFIRMED → COMPLETED, by the owner of the booking's business.</summary>
    /// <exception cref="BookingNotFoundException">The booking doesn't exist.</exception>
    /// <exception cref="BookingAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    /// <exception cref="BookingRuleViolationException">The booking isn't CONFIRMED.</exception>
    Task<BookingDto> CompleteAsync(Guid bookingId, Guid callerId, CancellationToken ct = default);

    /// <summary>CONFIRMED → NO_SHOW, by the owner of the booking's business.</summary>
    /// <exception cref="BookingNotFoundException">The booking doesn't exist.</exception>
    /// <exception cref="BookingAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    /// <exception cref="BookingRuleViolationException">The booking isn't CONFIRMED.</exception>
    Task<BookingDto> MarkNoShowAsync(Guid bookingId, Guid callerId, CancellationToken ct = default);

    /// <summary>A page of <paramref name="userId"/>'s own bookings (bookings.user_id), most recent first.</summary>
    Task<PaginatedResponse<BookingDto>> ListOwnAsync(
        Guid userId, BookingStatus? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// The booking, if <paramref name="callerId"/> is its client or the owner of its business, or
    /// <paramref name="callerIsSuperAdmin"/>.
    /// </summary>
    /// <exception cref="BookingNotFoundException">The booking doesn't exist.</exception>
    /// <exception cref="BookingAccessDeniedException">The caller is none of the above.</exception>
    Task<BookingDto> GetAsync(Guid bookingId, Guid callerId, bool callerIsSuperAdmin, CancellationToken ct = default);

    /// <summary>
    /// A page of the business's bookings, earliest first, for its owner — even if the business is
    /// inactive, so past bookings stay readable. <paramref name="from"/>/<paramref name="to"/> are
    /// inclusive dates in the business's time zone (America/Costa_Rica), matched against startAt.
    /// </summary>
    /// <exception cref="BusinessNotFoundException">The business doesn't exist.</exception>
    /// <exception cref="BookingAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    Task<PaginatedResponse<BookingDto>> ListBusinessAsync(
        Guid businessId, Guid callerId, BookingStatus? status, DateOnly? from, DateOnly? to,
        int page, int pageSize, CancellationToken ct = default);
}
