namespace Spot.Business.Api.Repositories;

/// <summary>
/// Raised when trg_validate_review rejects the insert: the booking isn't COMPLETED, or (only in a race,
/// since the service takes both from the booking) its user or business don't match the review's.
/// </summary>
public sealed class BookingNotReviewableException(Guid bookingId, Exception? innerException = null)
    : Exception($"Booking '{bookingId}' can't be reviewed.", innerException)
{
    public Guid BookingId { get; } = bookingId;
}
