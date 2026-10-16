using Spot.Business.Api.DTOs;

namespace Spot.Business.Api.Services;

/// <summary>A business's offered services (contracts/spot-api.yaml, "BUSINESS - Services").</summary>
public interface IServiceOfferingService
{
    /// <summary>
    /// Creates a service, always active, under <paramref name="businessId"/>. Null if no business
    /// with <paramref name="businessId"/> exists (active or not).
    /// </summary>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    Task<ServiceDto?> CreateAsync(Guid businessId, Guid callerId, ServiceCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Partially updates a service (only the fields present in <paramref name="request"/>). Null if
    /// no service with <paramref name="serviceId"/> exists (404). <c>isActive</c> can reactivate a
    /// deactivated service; the owner can update even if the business is inactive, same as create.
    /// </summary>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the service's business.</exception>
    Task<ServiceDto?> UpdateAsync(Guid serviceId, Guid callerId, ServiceUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Soft delete (<c>is_active = false</c>) — never a physical delete, to keep booking history
    /// intact. Idempotent: deactivating an already inactive service still succeeds. The owner can
    /// deactivate even if the business is inactive.
    /// </summary>
    /// <returns>False if no service with <paramref name="serviceId"/> exists (404); true if deactivated.</returns>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the service's business.</exception>
    Task<bool> DeactivateAsync(Guid serviceId, Guid callerId, CancellationToken ct = default);
}
