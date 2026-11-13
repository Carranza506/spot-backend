using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.Repositories;

/// <summary>Read-only queries behind availability (#66). All results are untracked.</summary>
public interface IAvailabilityRepository
{
    /// <summary>The business with <paramref name="businessId"/>, active or not, or null if none exists.</summary>
    Task<BusinessReference?> GetBusinessAsync(Guid businessId, CancellationToken ct = default);

    /// <summary>The service with <paramref name="serviceId"/>, of any business and active or not, or null if none exists.</summary>
    Task<ServiceReference?> GetServiceAsync(Guid serviceId, CancellationToken ct = default);

    /// <summary>The business's weekly schedule (at most one row per day of week).</summary>
    Task<IReadOnlyList<BusinessHoursReference>> GetWeeklyHoursAsync(Guid businessId, CancellationToken ct = default);

    /// <summary>The business's schedule exceptions dated within [<paramref name="from"/>, <paramref name="to"/>] (inclusive).</summary>
    Task<IReadOnlyList<BusinessScheduleExceptionReference>> GetScheduleExceptionsAsync(
        Guid businessId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The business's active bookings (status PENDING or CONFIRMED, the same set
    /// excl_active_booking_overlap covers) that overlap [<paramref name="startUtc"/>,
    /// <paramref name="endUtc"/>), loaded in a single query for the whole requested range.
    /// Both bounds must be UTC (offset 0), as Npgsql requires for timestamptz parameters.
    /// </summary>
    Task<IReadOnlyList<BookedInterval>> GetActiveBookingsAsync(
        Guid businessId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default);
}
