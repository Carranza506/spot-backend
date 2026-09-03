using Spot.AiSearch.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Services;

public interface IAiRequestService
{
    Task<PaginatedResponse<AiRequestLogDto>> ListAsync(AiSearchFilterQuery filter, CancellationToken ct = default);
}
