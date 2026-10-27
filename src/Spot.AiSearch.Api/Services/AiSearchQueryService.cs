using System.Net;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Services.Business;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Services;

public sealed class AiSearchQueryService(
    IGeminiClient gemini,
    IAiRequestService aiRequestService,
    IBusinessSearchClient businessSearch,
    ILogger<AiSearchQueryService> logger) : IAiSearchQueryService
{
    /// <summary>ai_requests.error_code when the model answered but nothing usable came out (the 422).</summary>
    public const string NotUnderstoodErrorCode = "AI_SEARCH_NOT_UNDERSTOOD";

    /// <summary>ai_requests.error_code when the model answered but Spot.Business.Api couldn't run the search.</summary>
    public const string BusinessSearchFailedErrorCode = "BUSINESS_SEARCH_FAILED";

    public async Task<AiSearchResultDto> SearchAsync(
        AiSearchQueryRequest request, Guid? userId, IPAddress? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var query = request.Query!.Trim();

        var extraction = await gemini.ExtractSearchCriteriaAsync(query, ct);
        // GeminiClient hands ownership of the document to the caller; RecordAsync keeps its own copy.
        using var extracted = extraction.ExtractedParameters;

        // Every call is stored exactly once (#74), once its outcome is known, so the row says why it
        // failed. status keeps describing the Gemini call itself; a later failure goes in error_code.
        Task<AiRequestSummaryDto> RecordAsync(GeminiExtractionResult outcome) =>
            aiRequestService.RecordAsync(query, outcome, userId, ipAddress, userAgent);

        if (extraction.Status != AiRequestStatus.SUCCESS || extracted is null)
        {
            await RecordAsync(extraction);
            throw new AiSearchUnavailableException(extraction.ErrorCode);
        }

        var criteria = AiSearchCriteriaParser.Parse(extracted.RootElement);
        var location = request.Location;

        // Date, time and budget alone can't narrow a business search (it filters by text, category
        // and proximity only), so without any of those three there's nothing meaningful to run.
        if (criteria.ServiceQuery is null && criteria.CategoryId is null && location is null)
        {
            var notUnderstood = new AiSearchNotUnderstoodException();
            await RecordAsync(extraction with { ErrorCode = NotUnderstoodErrorCode, ErrorMessage = notUnderstood.Message });
            throw notUnderstood;
        }

        // Date, time and budget are returned in `criteria` but not applied: GET /business/businesses
        // has no price or availability filter (#73, first PR). Only real search results come back.
        PaginatedResponse<BusinessDto> businesses;
        try
        {
            businesses = await businessSearch.SearchAsync(
                new BusinessSearchRequest(
                    Q: criteria.ServiceQuery,
                    CategoryId: criteria.CategoryId,
                    Latitude: location?.Latitude,
                    Longitude: location?.Longitude,
                    RadiusKm: location is null ? null : request.RadiusKm,
                    Page: request.Page,
                    PageSize: request.PageSize),
                ct);
        }
        catch (BusinessSearchUnavailableException ex)
        {
            await RecordAsync(extraction with { ErrorCode = BusinessSearchFailedErrorCode, ErrorMessage = ex.Message });
            throw;
        }
        catch
        {
            // Anything else (e.g. the client went away mid-search): Gemini already answered and was
            // billed, so the call is still audited before the exception goes on.
            await RecordAsync(extraction);
            throw;
        }

        var aiRequest = await RecordAsync(extraction);

        logger.LogInformation(
            "AI search {AiRequestId} returned {Count} of {Total} businesses.",
            aiRequest.Id, businesses.Data.Count, businesses.Pagination.Total);

        return new AiSearchResultDto(criteria, businesses, aiRequest);
    }
}
