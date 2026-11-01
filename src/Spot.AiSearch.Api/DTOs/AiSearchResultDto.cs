using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.DTOs;

/// <summary>Matches the "AiSearchResult" schema in contracts/spot-api.yaml.</summary>
public record AiSearchResultDto(
    AiSearchCriteriaDto Criteria,
    PaginatedResponse<BusinessDto> Businesses,
    AiRequestSummaryDto AiRequest
);
