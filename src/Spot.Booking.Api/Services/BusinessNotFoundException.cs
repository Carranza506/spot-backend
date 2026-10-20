namespace Spot.Booking.Api.Services;

/// <summary>Raised when availability is requested for a business that doesn't exist or is inactive.</summary>
public sealed class BusinessNotFoundException(Guid businessId)
    : Exception($"Business '{businessId}' does not exist or is inactive.")
{
    public Guid BusinessId { get; } = businessId;
}
