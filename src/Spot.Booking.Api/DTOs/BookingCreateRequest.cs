using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Spot.Booking.Api.DTOs;

/// <summary>
/// Matches the BookingCreateRequest schema in contracts/spot-api.yaml. The required fields are
/// nullable so a missing (or null) one fails [Required] (400) instead of binding to a default.
/// </summary>
public sealed class BookingCreateRequest
{
    [Required]
    public Guid? BusinessId { get; set; }

    [Required]
    public Guid? ServiceId { get; set; }

    /// <summary>An RFC 3339 date-time with an explicit offset; see <see cref="OffsetDateTimeJsonConverter"/>.</summary>
    [Required]
    [JsonConverter(typeof(OffsetDateTimeJsonConverter))]
    public DateTimeOffset? StartAt { get; set; }

    public string? Notes { get; set; }
}
