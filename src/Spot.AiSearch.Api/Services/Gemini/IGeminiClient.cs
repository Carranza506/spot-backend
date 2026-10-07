namespace Spot.AiSearch.Api.Services.Gemini;

/// <summary>
/// Stateless client for the Gemini API. Translates a free-text search query into the structured
/// criteria (service, date, time, budget) defined by the <c>AiSearchCriteria</c> schema in
/// contracts/spot-api.yaml. Never touches the database — see issue #45.
/// </summary>
public interface IGeminiClient
{
    Task<GeminiExtractionResult> ExtractSearchCriteriaAsync(string query, CancellationToken ct = default);
}
