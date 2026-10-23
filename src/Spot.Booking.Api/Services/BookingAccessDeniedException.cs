namespace Spot.Booking.Api.Services;

/// <summary>
/// Raised when the caller may not read a booking (it isn't the booking's client, the owner of its
/// business, or a SUPERADMIN) or a business's bookings (it isn't the business's owner).
/// </summary>
public sealed class BookingAccessDeniedException()
    : Exception("The caller is not allowed to read this booking or business's bookings.");
