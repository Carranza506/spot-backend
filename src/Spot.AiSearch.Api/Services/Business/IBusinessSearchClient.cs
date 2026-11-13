using Spot.AiSearch.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Services.Business;

/// <summary>
/// The filters forwarded to Spot.Business.Api's GET /business/businesses (#118/#119). Proximity
/// is all-or-nothing there: <see cref="Latitude"/>, <see cref="Longitude"/> and
/// <see cref="RadiusKm"/> are sent only when all three are set.
/// </summary>
public sealed record BusinessSearchRequest(
    string? Q,
    Guid? CategoryId,
    double? Latitude,
    double? Longitude,
    double? RadiusKm,
    int Page,
    int PageSize);

/// <summary>
/// Client for Spot.Business.Api's public business search. The search itself lives in Business
/// (text, category, PostGIS proximity), so AiSearch reuses it instead of duplicating it — see #73.
/// </summary>
public interface IBusinessSearchClient
{
    /// <exception cref="BusinessSearchUnavailableException">Business is down, timed out, or answered with an error or an unreadable body.</exception>
    Task<PaginatedResponse<BusinessDto>> SearchAsync(BusinessSearchRequest request, CancellationToken ct = default);
}
