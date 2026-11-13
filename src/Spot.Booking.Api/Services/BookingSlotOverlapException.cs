namespace Spot.Booking.Api.Services;

/// <summary>
/// Raised when inserting a booking violates excl_active_booking_overlap: its time range overlaps
/// another active (PENDING/CONFIRMED) booking of the same business.
/// </summary>
public sealed class BookingSlotOverlapException(Exception? innerException = null)
    : Exception("The booking overlaps another active booking of the same business.", innerException);
