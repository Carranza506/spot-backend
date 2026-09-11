using System.Text.Json;

namespace Spot.AiSearch.Api.DTOs;

public record AiRequestLogDto(
    Guid Id,
    Guid? UserId,
    string Provider,
    string Model,
    string RequestType,
    string Prompt,
    string? Response,
    JsonElement? ExtractedParameters,
    JsonElement? ToolCalls,
    string Status,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    int? LatencyMs,
    string? ErrorCode,
    string? ErrorMessage,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAt
);
