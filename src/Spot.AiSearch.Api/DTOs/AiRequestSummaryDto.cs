namespace Spot.AiSearch.Api.DTOs;

/// <summary>Matches the "AiRequestSummary" schema in contracts/spot-api.yaml.</summary>
public record AiRequestSummaryDto(
    Guid Id,
    string Status,
    string RequestType,
    int? TotalTokens,
    int? LatencyMs,
    DateTime CreatedAt
);
