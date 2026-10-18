using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Spot.Booking.Api.DTOs;

/// <summary>
/// Reads a date-time only if it carries an explicit offset ("Z" or ±hh:mm), as RFC 3339 (the
/// contract's <c>format: date-time</c>) requires. The default converter would silently read
/// "2026-09-01T14:00:00" in the server's own time zone; here it fails binding (400) instead.
/// </summary>
public sealed partial class OffsetDateTimeJsonConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !ExplicitOffset().IsMatch(reader.GetString()!)
            || !reader.TryGetDateTimeOffset(out var value))
            throw new JsonException("Expected an RFC 3339 date-time with an explicit offset.");

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is { } v)
            writer.WriteStringValue(v);
        else
            writer.WriteNullValue();
    }

    [GeneratedRegex(@"T.*(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffset();
}
