namespace Spot.Business.Api.Repositories;

/// <summary>
/// The business already has <see cref="MaxServices"/> active services, the most its plan allows,
/// so another one can't be created or reactivated. Mapped to 409 SERVICE_LIMIT_REACHED by the controller.
/// </summary>
public sealed class ServiceLimitReachedException(int maxServices)
    : Exception($"The limit of {maxServices} active services has been reached.")
{
    public int MaxServices { get; } = maxServices;
}
