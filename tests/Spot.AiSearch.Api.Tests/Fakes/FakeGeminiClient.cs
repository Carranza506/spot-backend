using System.Text.Json;
using Spot.AiSearch.Api.Services.Gemini;

namespace Spot.AiSearch.Api.Tests.Fakes;

/// <summary>Stands in for the Gemini API in pipeline tests: answers with whatever <see cref="Respond"/> returns.</summary>
public sealed class FakeGeminiClient : IGeminiClient
{
    public Func<string, GeminiExtractionResult> Respond { get; set; } = _ => Success("{}");

    public string? LastQuery { get; private set; }

    public Task<GeminiExtractionResult> ExtractSearchCriteriaAsync(string query, CancellationToken ct = default)
    {
        LastQuery = query;
        return Task.FromResult(Respond(query));
    }

    public void Reset()
    {
        Respond = _ => Success("{}");
        LastQuery = null;
    }

    /// <summary>A SUCCESS result whose extracted parameters are <paramref name="json"/>.</summary>
    public static GeminiExtractionResult Success(string json) =>
        GeminiExtractionResult.Success(json, JsonDocument.Parse(json), inputTokens: 40, outputTokens: 20, totalTokens: 60, latencyMs: 300);
}
