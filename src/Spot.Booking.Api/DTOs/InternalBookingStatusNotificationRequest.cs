using System.Text.Json.Serialization;
using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.DTOs;

/// <summary>
/// Matches the InternalBookingStatusNotificationRequest schema in contracts/spot-api.yaml. The
/// optional message is left out: Spot.Notifications.Api then uses its default text per status.
/// </summary>
public sealed record InternalBookingStatusNotificationRequest(
    Guid BookingId,
    Guid BusinessId,
    Guid UserId,
    [property: JsonConverter(typeof(BookingStatusJsonConverter))] BookingStatus Status);
