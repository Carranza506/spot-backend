namespace Spot.Booking.Api.Services;

/// <summary>
/// Raised when availability or a new booking is requested for a business that doesn't exist or is
/// inactive, or when a business's bookings are listed for a business that doesn't exist.
/// </summary>
public sealed class BusinessNotFoundException(Guid businessId)
    : Exception($"Business '{businessId}' does not exist or is inactive.")
{
    public Guid BusinessId { get; } = businessId;
}
