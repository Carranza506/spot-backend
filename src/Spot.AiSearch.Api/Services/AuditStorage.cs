using System.Buffers;
using System.Text.Json;
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
        ExtractedParameters = result.ExtractedParameters is { } document ? WithoutNul(document.RootElement) : null,
    };

    public static string? WithoutNul(string? text) => text?.Replace('\0', ' ');

    // Copies the JSON token by token instead of editing its text, so an escaped backslash followed by
    // "u0000" (a legitimate string) is never mistaken for a NUL escape. Property names are cleaned too.
    // A key the model repeated is copied as is: JsonNode would throw on it and fail the audit row.
    private static JsonDocument WithoutNul(JsonElement root)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            Write(writer, root);
        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(WithoutNul(property.Name)!);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(WithoutNul(element.GetString()));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
