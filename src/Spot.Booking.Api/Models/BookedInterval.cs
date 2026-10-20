namespace Spot.Booking.Api.Models;

/// <summary>The time span and status of an existing booking, as needed to subtract it from availability.</summary>
public sealed record BookedInterval(DateTimeOffset StartAt, DateTimeOffset EndAt, BookingStatus Status);
