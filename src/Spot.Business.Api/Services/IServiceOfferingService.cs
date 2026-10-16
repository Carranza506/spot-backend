using Spot.Business.Api.DTOs;
using Spot.Shared.Pagination;

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
    /// A page of the business's services. Owner-aware: the <b>owner</b> (<paramref name="callerId"/>
    /// equal to the business's account) sees all their services and <paramref name="isActive"/>
    /// filters freely (omitted = all); everyone else (public or non-owner) sees only active ones,
    /// and an inactive business is 404 for them. Null when the business doesn't exist, or is
    /// inactive and the caller isn't its owner (treated as 404). <paramref name="callerId"/> is the
    /// JWT sub when a token was sent, or null for anonymous callers.
    /// </summary>
    Task<PaginatedResponse<ServiceDto>?> ListAsync(
        Guid businessId, int page, int pageSize, bool? isActive, Guid? callerId, CancellationToken ct = default);

    /// <summary>
    /// A single service. Owner-aware: the owner sees their own service even if it (or its business)
    /// is inactive; everyone else gets null (404) for an inactive service or a service of an
    /// inactive business. <paramref name="callerId"/> is the JWT sub, or null for anonymous callers.
    /// </summary>
    Task<ServiceDto?> GetAsync(Guid serviceId, Guid? callerId, CancellationToken ct = default);
}
