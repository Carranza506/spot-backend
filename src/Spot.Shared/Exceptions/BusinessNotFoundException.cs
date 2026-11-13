namespace Spot.Shared.Exceptions;

/// <summary>
/// Raised when the businessId given can't be used for the request. Each service decides what that
/// covers (in Spot.Booking.Api an inactive business too, for availability and new bookings); see the
/// throwing method's docs. Controllers answer it with a 404.
/// </summary>
public sealed class BusinessNotFoundException(Guid businessId)
    : Exception($"Business '{businessId}' was not found.")
{
    public Guid BusinessId { get; } = businessId;
}
