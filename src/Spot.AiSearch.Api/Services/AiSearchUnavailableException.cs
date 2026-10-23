namespace Spot.AiSearch.Api.Services;

/// <summary>The AI model call failed or timed out. Its ai_requests row (ERROR/TIMEOUT) is already stored.</summary>
public sealed class AiSearchUnavailableException(string? errorCode)
    : Exception($"The AI model call failed ({errorCode ?? "unknown"}).")
{
    public string? ErrorCode { get; } = errorCode;
}
