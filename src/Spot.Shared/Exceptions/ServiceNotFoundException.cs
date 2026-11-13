namespace Spot.Shared.Exceptions;

/// <summary>
/// Raised when the serviceId given can't be used for the request. Each service decides what that
/// covers (in Spot.Booking.Api an inactive service, or one of another business, too); see the
/// throwing method's docs. Controllers answer it with a 404.
/// </summary>
public sealed class ServiceNotFoundException(Guid serviceId)
    : Exception($"Service '{serviceId}' was not found.")
{
    public Guid ServiceId { get; } = serviceId;
}
