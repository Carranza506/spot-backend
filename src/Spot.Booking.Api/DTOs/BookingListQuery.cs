using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.DTOs;

/// <summary>
/// Query parameters for <c>GET /booking/bookings</c> (contract: PageParam/PageSizeParam, status).
/// <see cref="Status"/> is bound as a string on purpose: binding it as the enum would also accept
/// numbers and other casings. The controller parses it strictly (<see cref="TryParseStatus"/>).
/// </summary>
public class BookingListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Status { get; set; }

    /// <summary>An absent value is valid (no filter); a present one must be one of the contract's uppercase names.</summary>
    public static bool TryParseStatus(string? value, out BookingStatus? status)
    {
        status = null;
        if (value is null)
            return true;

        if (!Enum.GetNames<BookingStatus>().Contains(value, StringComparer.Ordinal))
            return false;

        status = Enum.Parse<BookingStatus>(value);
        return true;
    }
}

/// <summary>
/// Query parameters for <c>GET /booking/businesses/{businessId}/bookings</c>: the own-bookings ones
/// plus from/to. Those are strings for the same reason as Spot.Business.Api's
/// BusinessScheduleExceptionListQuery: binding DateOnly would accept formats other than YYYY-MM-DD.
/// </summary>
public sealed class BusinessBookingListQuery : BookingListQuery
{
    public const string DateFormat = "yyyy-MM-dd";

    public string? From { get; set; }
    public string? To { get; set; }
}
