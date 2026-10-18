namespace Spot.Booking.Api.Services;

/// <summary>Raised when the serviceId given doesn't exist, is inactive, or belongs to another business.</summary>
public sealed class ServiceNotFoundException(Guid serviceId)
    : Exception($"Service '{serviceId}' does not exist, is inactive, or belongs to another business.")
{
    public Guid ServiceId { get; } = serviceId;
}
