using System.Net;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Services.Business;

namespace Spot.AiSearch.Api.Services;

public interface IAiSearchQueryService
{
    /// <summary>
    /// POST /ai-search/query (#73): the AI model extracts criteria from <paramref name="request"/>'s
    /// query, the call is always stored in ai_requests (failures included), and the business search
    /// itself runs in Spot.Business.Api. The businesses returned come only from that search.
    /// <paramref name="request"/> must already be validated.
    /// </summary>
    /// <exception cref="AiSearchUnavailableException">The AI model call failed or timed out.</exception>
    /// <exception cref="AiSearchNotUnderstoodException">No service, category or location to search with.</exception>
    /// <exception cref="BusinessSearchUnavailableException">Spot.Business.Api couldn't run the search.</exception>
    Task<AiSearchResultDto> SearchAsync(
        AiSearchQueryRequest request, Guid? userId, IPAddress? ipAddress, string? userAgent, CancellationToken ct = default);
}
