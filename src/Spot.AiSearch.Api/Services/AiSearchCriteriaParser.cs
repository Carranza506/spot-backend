using System.Globalization;
using System.Text.Json;
using Spot.AiSearch.Api.DTOs;

namespace Spot.AiSearch.Api.Services;

/// <summary>
/// Turns the JSON the AI model returned into <see cref="AiSearchCriteriaDto"/> without trusting it:
/// a field that's missing, of the wrong type or invalid (an impossible date, a malformed time, a
/// negative budget, a blank string) becomes null instead of failing the search or reaching the response.
/// Text never keeps control characters (NUL included), and <c>serviceQuery</c> is capped.
/// </summary>
public static class AiSearchCriteriaParser
{
    /// <summary>
    /// Longest <c>serviceQuery</c> kept: it's sent to Business as <c>q</c>, matched against business
    /// names (contract maxLength 150) and descriptions, so a longer term adds nothing and only
    /// inflates the URL — a prompt-injected value could otherwise push it past the server's limit.
    /// </summary>
    public const int ServiceQueryMaxLength = 150;

    private static readonly string[] TimeFormats = ["HH:mm:ss", "HH:mm"];

    public static AiSearchCriteriaDto Parse(JsonElement extracted)
    {
        if (extracted.ValueKind != JsonValueKind.Object)
            return new AiSearchCriteriaDto(null, null, null, null, null, null);

        return new AiSearchCriteriaDto(
            ServiceQuery: Truncate(ReadText(extracted, "serviceQuery"), ServiceQueryMaxLength),
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

    // Control characters (NUL, CR/LF, tabs…) become spaces rather than being dropped, so words they
    // separated don't run together. NUL in particular can't be sent to Postgres in a text value.
    private static string? ReadText(JsonElement extracted, string propertyName) =>
        extracted.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && string.Concat(value.GetString()!.Select(c => char.IsControl(c) ? ' ' : c)).Trim() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>Cuts <paramref name="text"/> to <paramref name="maxLength"/> without splitting a surrogate pair (e.g. an emoji).</summary>
    private static string? Truncate(string? text, int maxLength)
    {
        if (text is null || text.Length <= maxLength)
            return text;

        var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..length].TrimEnd();
    }
}
