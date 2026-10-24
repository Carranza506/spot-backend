using System.Globalization;
using System.Text.Json;
using Spot.AiSearch.Api.DTOs;

namespace Spot.AiSearch.Api.Services;

/// <summary>
/// Turns the JSON the AI model returned into <see cref="AiSearchCriteriaDto"/> without trusting it:
/// a field that's missing, of the wrong type or invalid (an impossible date, a malformed time, a
/// negative budget, a blank string) becomes null instead of failing the search or reaching the response.
/// </summary>
public static class AiSearchCriteriaParser
{
    private static readonly string[] TimeFormats = ["HH:mm:ss", "HH:mm"];

    public static AiSearchCriteriaDto Parse(JsonElement extracted)
    {
        if (extracted.ValueKind != JsonValueKind.Object)
            return new AiSearchCriteriaDto(null, null, null, null, null, null);

        return new AiSearchCriteriaDto(
            ServiceQuery: ReadText(extracted, "serviceQuery"),
            // Not resolved yet (#73, first PR): the model can't know category ids, and matching its
            // text against the categories is a follow-up. The contract allows null.
            CategoryId: null,
            Date: ReadText(extracted, "date") is { } date
                && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                    ? parsedDate
                    : null,
            Time: ReadText(extracted, "time") is { } time
                && TimeOnly.TryParseExact(time, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
                    ? parsedTime
                    : null,
            BudgetMaxColones: extracted.TryGetProperty("budgetMaxColones", out var budget)
                && budget.ValueKind == JsonValueKind.Number
                && budget.TryGetDecimal(out var amount)
                && amount >= 0
                    ? amount
                    : null,
            RawLocationText: ReadText(extracted, "rawLocationText"));
    }

    private static string? ReadText(JsonElement extracted, string propertyName) =>
        extracted.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : null;
}
