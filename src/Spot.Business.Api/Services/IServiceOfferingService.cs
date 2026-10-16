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
    /// Public: a page of the business's services. By default only active ones; pass
    /// <paramref name="isActive"/> to filter explicitly. Null if no business with
    /// <paramref name="businessId"/> exists or it's inactive (treated as 404, same visibility as
    /// <see cref="IBusinessService.GetPublicAsync"/>).
    /// </summary>
    Task<PaginatedResponse<ServiceDto>?> ListAsync(
        Guid businessId, int page, int pageSize, bool? isActive, CancellationToken ct = default);

    /// <summary>
    /// Public: a single service. Null (treated as 404) if it doesn't exist, is inactive, or its
    /// business is inactive — a deactivated service isn't publicly visible.
    /// </summary>
    Task<ServiceDto?> GetAsync(Guid serviceId, CancellationToken ct = default);
}
