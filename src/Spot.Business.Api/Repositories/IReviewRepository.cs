using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public interface IReviewRepository
{
    /// <summary>The booking's owner and business, or null if there's no booking with that id.</summary>
    Task<BookingReference?> GetBookingAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Inserts the review; its id and timestamps are the database's.</summary>
    /// <exception cref="ReviewAlreadyExistsException">The booking already has a review (SQLSTATE 23505).</exception>
    /// <exception cref="BookingNotReviewableException">trg_validate_review rejected it (SQLSTATE P0001).</exception>
    Task CreateAsync(Review review, CancellationToken ct = default);

    /// <summary>A page of <paramref name="businessId"/>'s reviews, newest first, plus the total count.</summary>
    Task<(IReadOnlyList<Review> Items, int Total)> ListByBusinessAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);
}
