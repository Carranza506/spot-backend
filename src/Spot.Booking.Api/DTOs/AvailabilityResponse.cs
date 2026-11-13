namespace Spot.Booking.Api.DTOs;

/// <summary>Matches the AvailabilityResponse schema in contracts/spot-api.yaml. <see cref="ServiceId"/> is null when no serviceId was sent.</summary>
public sealed record AvailabilityResponse(
    Guid BusinessId, Guid? ServiceId, DateOnly From, DateOnly To, IReadOnlyList<AvailabilityDay> Days);

/// <summary>AvailabilityDay: <see cref="Date"/> is the business's local date (America/Costa_Rica).</summary>
public sealed record AvailabilityDay(DateOnly Date, bool IsClosed, IReadOnlyList<AvailabilitySlot> Slots);

/// <summary>
/// AvailabilitySlot. Both values are UTC <see cref="DateTime"/>s (Kind = Utc), so they serialize
/// with a "Z" suffix as in the contract example ("2026-09-01T14:00:00Z").
/// </summary>
public sealed record AvailabilitySlot(DateTime StartAt, DateTime EndAt);
