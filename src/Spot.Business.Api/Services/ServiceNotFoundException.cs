namespace Spot.Business.Api.Services;

/// <summary>Raised when an operation on a resource nested under a service (e.g. its photos) targets a service that doesn't exist.</summary>
public sealed class ServiceNotFoundException(Guid serviceId)
    : Exception($"Service '{serviceId}' does not exist.")
{
    public Guid ServiceId { get; } = serviceId;
}
