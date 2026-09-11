using System.Net;
using System.Text.Json;

namespace Spot.AiSearch.Api.Models;

public class AiRequest
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Provider { get; set; } = default!;
    public string Model { get; set; } = default!;
    public AiRequestType RequestType { get; set; }
    public string Prompt { get; set; } = default!;
    public string? Response { get; set; }
    public JsonDocument? ExtractedParameters { get; set; }
    public JsonDocument? ToolCalls { get; set; }
    public AiRequestStatus Status { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int? LatencyMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
