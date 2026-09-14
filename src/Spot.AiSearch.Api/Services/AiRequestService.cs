using System.Text.Json;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Services;

public class AiRequestService(IAiRequestRepository repository) : IAiRequestService
{
    public async Task<PaginatedResponse<AiRequestLogDto>> ListAsync(
        AiSearchFilterQuery filter, CancellationToken ct = default)
    {
        var (items, total) = await repository.ListAsync(filter, ct);

        var dtos = items.Select(x => new AiRequestLogDto(
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
        )).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)filter.PageSize);
        var pagination = new PaginationMeta(filter.Page, filter.PageSize, total, totalPages);

        return new PaginatedResponse<AiRequestLogDto>(dtos, pagination);
    }
}
