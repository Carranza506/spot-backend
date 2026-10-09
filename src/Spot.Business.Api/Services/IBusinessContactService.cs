using Spot.Business.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public interface IBusinessContactService
{
    /// <summary>Public listing: null if the business doesn't exist or is inactive (same visibility as GET /business/businesses/{businessId}).</summary>
    Task<PaginatedResponse<BusinessContactDto>?> ListAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <exception cref="BusinessNotFoundException">The business doesn't exist.</exception>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    Task<BusinessContactDto> CreateAsync(
        Guid businessId, Guid callerId, BusinessContactCreateRequest request, CancellationToken ct = default);

    /// <summary>Hard delete.</summary>
    /// <returns>False if the contact doesn't exist or belongs to a different business.</returns>
    /// <exception cref="BusinessNotFoundException">The business doesn't exist.</exception>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    Task<bool> DeleteAsync(Guid businessId, Guid contactId, Guid callerId, CancellationToken ct = default);
}
