using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.DTOs;

/// <summary>
/// Matches the InternalBookingStatusNotificationRequest schema in contracts/spot-api.yaml. The
/// required fields are nullable so a missing (or null) one fails [Required] (400) instead of
/// binding to a default.
/// </summary>
public sealed class InternalBookingStatusNotificationRequest
{
    [Required]
    public Guid? BookingId { get; set; }

    [Required]
    public Guid? BusinessId { get; set; }

    [Required]
    public Guid? UserId { get; set; }

    [Required]
    [JsonConverter(typeof(BookingStatusJsonConverter))]
    public BookingStatus? Status { get; set; }

    public string? Message { get; set; }
}

/// <summary>
/// Matches the member names only: JsonStringEnumConverter also accepts comma-separated lists, and
/// "CONFIRMED,CANCELLED" would bind to CANCELLED.
/// </summary>
public sealed class BookingStatusJsonConverter : JsonConverter<BookingStatus>
{
    public override BookingStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Enum.GetNames<BookingStatus>().Contains(reader.GetString())
            ? Enum.Parse<BookingStatus>(reader.GetString()!)
            : throw new JsonException("Not a BookingStatus.");

    public override void Write(Utf8JsonWriter writer, BookingStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
