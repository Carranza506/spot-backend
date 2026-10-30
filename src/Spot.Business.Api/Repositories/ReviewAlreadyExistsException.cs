namespace Spot.Business.Api.Repositories;

/// <summary>Raised when a booking that already has a review gets another one (reviews.booking_id is unique).</summary>
public sealed class ReviewAlreadyExistsException(Guid bookingId, Exception? innerException = null)
    : Exception($"Booking '{bookingId}' already has a review.", innerException)
{
    public Guid BookingId { get; } = bookingId;
}
