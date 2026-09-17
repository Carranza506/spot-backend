using System.Text.Json;
using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.Services.Gemini;

/// <summary>
/// Outcome of a single Gemini call, carrying everything a caller needs to persist an
/// <see cref="AiRequest"/> row without the client having to know about the database — it stays a
/// stateless natural-language parser, per the architecture decision in issue #45.
/// </summary>
public sealed record GeminiExtractionResult(
    AiRequestStatus Status,
    string? RawResponse,
    JsonDocument? ExtractedParameters,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    int LatencyMs,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static GeminiExtractionResult Success(
        string rawResponse,
        JsonDocument extractedParameters,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        int latencyMs) => new(
            AiRequestStatus.SUCCESS, rawResponse, extractedParameters,
            inputTokens, outputTokens, totalTokens, latencyMs, null, null);

    public static GeminiExtractionResult Timeout(int latencyMs, string errorMessage) => new(
        AiRequestStatus.TIMEOUT, null, null, null, null, null, latencyMs, "GEMINI_TIMEOUT", errorMessage);

    public static GeminiExtractionResult Error(int latencyMs, string errorCode, string errorMessage) => new(
        AiRequestStatus.ERROR, null, null, null, null, null, latencyMs, errorCode, errorMessage);
}
