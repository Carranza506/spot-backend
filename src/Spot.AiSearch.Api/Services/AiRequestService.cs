using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Options;
using Spot.AiSearch.Api.Repositories;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Services;

public class AiRequestService(
    IAiRequestRepository repository,
    IOptions<GeminiOptions> geminiOptions,
    ILogger<AiRequestService> logger) : IAiRequestService
{
    private const string Provider = "gemini";

    public async Task<AiRequestSummaryDto> RecordAsync(
        string prompt, GeminiExtractionResult result, Guid? userId, IPAddress? ipAddress, string? userAgent)
    {
        var entity = new AiRequest
        {
            UserId = userId,
            Provider = Provider,
            Model = geminiOptions.Value.Model,
            RequestType = AiRequestType.BUSINESS_SEARCH,
            Prompt = prompt,
            Response = result.RawResponse,
            // A copy, so the caller still owns (and may dispose) the result's document.
            ExtractedParameters = result.ExtractedParameters is not null
                ? JsonDocument.Parse(result.ExtractedParameters.RootElement.GetRawText())
                : null,
            ToolCalls = null,
            Status = result.Status,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            TotalTokens = result.TotalTokens,
            LatencyMs = result.LatencyMs,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            // No DB default in the EF mapping, and Npgsql needs Kind=Utc for timestamptz.
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            // CancellationToken.None, not the request's: Gemini already answered (and was billed),
            // so a client disconnect must not cancel the audit row.
            await repository.AddAsync(entity, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to persist ai_requests row for a Gemini call with status {Status} and error_code {ErrorCode}.",
                result.Status, result.ErrorCode);
            throw;
        }

        return new AiRequestSummaryDto(
            Id: entity.Id,
            Status: entity.Status.ToString(),
            RequestType: entity.RequestType.ToString(),
            TotalTokens: entity.TotalTokens,
            LatencyMs: entity.LatencyMs,
            CreatedAt: entity.CreatedAt);
    }

    public async Task<PaginatedResponse<AiRequestLogDto>> ListAsync(
        AiSearchFilterQuery filter, CancellationToken ct = default)
    {
        var (items, total) = await repository.ListAsync(filter, ct);

        var dtos = items.Select(ToLogDto).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)filter.PageSize);
        var pagination = new PaginationMeta(filter.Page, filter.PageSize, total, totalPages);

        return new PaginatedResponse<AiRequestLogDto>(dtos, pagination);
    }

    public async Task<AiRequestLogDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repository.GetByIdAsync(id, ct);
        return entity is null ? null : ToLogDto(entity);
    }

    private static AiRequestLogDto ToLogDto(AiRequest x) => new(
        Id: x.Id,
        UserId: x.UserId,
        Provider: x.Provider,
        Model: x.Model,
        RequestType: x.RequestType.ToString(),
        Prompt: x.Prompt,
        Response: x.Response,
        ExtractedParameters: x.ExtractedParameters is not null
            ? x.ExtractedParameters.RootElement.Clone()
            : null,
        ToolCalls: x.ToolCalls is not null
            ? x.ToolCalls.RootElement.Clone()
            : null,
        Status: x.Status.ToString(),
        InputTokens: x.InputTokens,
        OutputTokens: x.OutputTokens,
        TotalTokens: x.TotalTokens,
        LatencyMs: x.LatencyMs,
        ErrorCode: x.ErrorCode,
        ErrorMessage: x.ErrorMessage,
        IpAddress: x.IpAddress?.ToString(),
        UserAgent: x.UserAgent,
        CreatedAt: x.CreatedAt
    );
}
