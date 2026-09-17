using System.Text.Json.Serialization;

namespace Spot.AiSearch.Api.Services.Gemini;

// Minimal shapes for the Gemini "generateContent" REST endpoint — only the fields GeminiClient
// actually reads or writes. Internal to this service; never exposed outside it.

internal sealed class GeminiGenerateContentRequest
{
    [JsonPropertyName("systemInstruction")]
    public GeminiContent SystemInstruction { get; set; } = default!;

    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = [];

    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig GenerationConfig { get; set; } = default!;
}

internal sealed class GeminiContent
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = [];
}

internal sealed class GeminiPart
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal sealed class GeminiGenerationConfig
{
    [JsonPropertyName("responseMimeType")]
    public string ResponseMimeType { get; set; } = "application/json";

    [JsonPropertyName("responseSchema")]
    public object ResponseSchema { get; set; } = default!;

    // This is a pure structured-extraction task, not one that benefits from multi-step reasoning
    // — thinking was measured burning ~90% of output tokens (added cost and latency) for no gain
    // in the extracted fields' quality.
    [JsonPropertyName("thinkingConfig")]
    public GeminiThinkingConfig ThinkingConfig { get; set; } = new();
}

internal sealed class GeminiThinkingConfig
{
    [JsonPropertyName("thinkingBudget")]
    public int ThinkingBudget { get; set; } = 0;
}

internal sealed class GeminiGenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

internal sealed class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int? PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")]
    public int? CandidatesTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public int? TotalTokenCount { get; set; }
}

internal sealed class GeminiErrorResponse
{
    [JsonPropertyName("error")]
    public GeminiErrorDetail? Error { get; set; }
}

internal sealed class GeminiErrorDetail
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
