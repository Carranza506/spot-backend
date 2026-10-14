using System.Net;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Services;

public interface IAiRequestService
{
    Task<PaginatedResponse<AiRequestLogDto>> ListAsync(AiSearchFilterQuery filter, CancellationToken ct = default);

    /// <summary>
    /// Persists one Gemini call (SUCCESS, ERROR or TIMEOUT alike) as an ai_requests row. Takes no
    /// cancellation token on purpose: once Gemini has answered, the row is written even if the
    /// client disconnects. <paramref name="result"/>'s ExtractedParameters is copied, never
    /// disposed, so the caller keeps owning it. A failed insert is logged and rethrown.
    /// </summary>
    Task<AiRequestSummaryDto> RecordAsync(
        string prompt, GeminiExtractionResult result, Guid? userId, IPAddress? ipAddress, string? userAgent);
}
