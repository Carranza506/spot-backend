using System.Text.Json.Serialization;
using Spot.Booking.Api.Models;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.DTOs;

/// <summary>
/// Matches the Booking schema in contracts/spot-api.yaml. Timestamps are UTC <see cref="DateTime"/>s,
/// so they serialize with a "Z" suffix like the availability slots (#66). <see cref="ClientName"/> is
/// read from users (#152), not stored on the booking: null when the account has no name.
/// </summary>
public sealed record BookingDto(
    Guid Id,
    Guid UserId,
    string? ClientName,
    Guid BusinessId,
    Guid ServiceId,
    DateTime StartAt,
    DateTime EndAt,
    [property: JsonConverter(typeof(BookingStatusJsonConverter))] BookingStatus Status,
    string ServiceName,
    decimal ServicePrice,
    int ServiceDurationMinutes,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static BookingDto FromEntity(BookingEntity booking, string? clientName) => new(
        booking.Id,
        booking.UserId,
        clientName,
        booking.BusinessId,
        booking.ServiceId,
        booking.StartAt.UtcDateTime,
        booking.EndAt.UtcDateTime,
        booking.Status,
        booking.ServiceName,
        booking.ServicePrice,
        booking.ServiceDurationMinutes,
        booking.Notes,
        booking.CreatedAt.UtcDateTime,
        booking.UpdatedAt.UtcDateTime);
}
