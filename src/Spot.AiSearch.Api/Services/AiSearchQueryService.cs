using System.Net;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Services.Business;
using Spot.AiSearch.Api.Services.Gemini;

namespace Spot.AiSearch.Api.Services;

public sealed class AiSearchQueryService(
    IGeminiClient gemini,
    IAiRequestService aiRequestService,
    IBusinessSearchClient businessSearch,
    ILogger<AiSearchQueryService> logger) : IAiSearchQueryService
{
    public async Task<AiSearchResultDto> SearchAsync(
        AiSearchQueryRequest request, Guid? userId, IPAddress? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var query = request.Query!.Trim();

        var extraction = await gemini.ExtractSearchCriteriaAsync(query, ct);
        // GeminiClient hands ownership of the document to the caller; RecordAsync keeps its own copy.
        using var extracted = extraction.ExtractedParameters;

        // Stored before deciding what to answer, so every call is audited: SUCCESS, ERROR and
        // TIMEOUT alike (#74), including the ones that end in a 422 or a Business failure below.
        var aiRequest = await aiRequestService.RecordAsync(query, extraction, userId, ipAddress, userAgent);

        if (extraction.Status != AiRequestStatus.SUCCESS || extracted is null)
            throw new AiSearchUnavailableException(extraction.ErrorCode);

        var criteria = AiSearchCriteriaParser.Parse(extracted.RootElement);
        var location = request.Location;

        // Date, time and budget alone can't narrow a business search (it filters by text, category
        // and proximity only), so without any of those three there's nothing meaningful to run.
        if (criteria.ServiceQuery is null && criteria.CategoryId is null && location is null)
            throw new AiSearchNotUnderstoodException();

        // Date, time and budget are returned in `criteria` but not applied: GET /business/businesses
        // has no price or availability filter (#73, first PR). Only real search results come back.
        var businesses = await businessSearch.SearchAsync(
            new BusinessSearchRequest(
                Q: criteria.ServiceQuery,
                CategoryId: criteria.CategoryId,
                Latitude: location?.Latitude,
                Longitude: location?.Longitude,
                RadiusKm: location is null ? null : request.RadiusKm,
                Page: request.Page,
                PageSize: request.PageSize),
            ct);

        logger.LogInformation(
            "AI search {AiRequestId} returned {Count} of {Total} businesses.",
            aiRequest.Id, businesses.Data.Count, businesses.Pagination.Total);

        return new AiSearchResultDto(criteria, businesses, aiRequest);
    }
}
