using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.AiSearch.Api.Services.Gemini;

namespace Spot.AiSearch.Api.Services;

/// <summary>
/// Makes a Gemini result storable in ai_requests: Postgres rejects NUL in text columns (prompt,
/// response, error_message) and the \u0000 escape in jsonb (extracted_parameters), so a model
/// answer carrying one would make the audit row fail to save. NUL becomes a space, as in the parser.
/// </summary>
public static class AuditStorage
{
    /// <summary>
    /// A copy of <paramref name="result"/> without NUL anywhere it's stored. Its ExtractedParameters
    /// is a new document the caller owns (dispose it); the original result is left untouched.
    /// </summary>
    public static GeminiExtractionResult WithoutNul(GeminiExtractionResult result) => result with
    {
        RawResponse = WithoutNul(result.RawResponse),
        ErrorMessage = WithoutNul(result.ErrorMessage),
        ExtractedParameters = result.ExtractedParameters is { } document
            ? JsonDocument.Parse(WithoutNul(JsonNode.Parse(document.RootElement.GetRawText()))?.ToJsonString() ?? "null")
            : null,
    };

    public static string? WithoutNul(string? text) => text?.Replace('\0', ' ');

    // Walks the JSON instead of editing its text, so an escaped backslash followed by "u0000" (a
    // legitimate string) is never mistaken for a NUL escape. Property names are cleaned too.
    private static JsonNode? WithoutNul(JsonNode? node) => node switch
    {
        JsonObject obj => obj.Aggregate(new JsonObject(), (clean, property) =>
        {
            clean[WithoutNul(property.Key)!] = WithoutNul(property.Value);
            return clean;
        }),
        JsonArray array => new JsonArray(array.Select(WithoutNul).ToArray()),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(WithoutNul(text)),
        _ => node?.DeepClone(),
    };
}
