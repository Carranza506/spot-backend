using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IReviewRepository — no database involved. Emulates the two database rules
/// ReviewRepository translates: trg_validate_review (only a COMPLETED booking, whose user and business
/// match the review's, can be reviewed) and the unique reviews.booking_id. Same ordering as the real one.
/// </summary>
public sealed class FakeReviewRepository : IReviewRepository
{
    private readonly Dictionary<Guid, BookingReference> _bookings = [];
    private readonly HashSet<Guid> _completed = [];
    private readonly Dictionary<Guid, Review> _reviews = [];

    public IReadOnlyCollection<Review> All => _reviews.Values;

    public void Reset()
    {
        _bookings.Clear();
        _completed.Clear();
        _reviews.Clear();
    }

    /// <summary>Seeds a booking of <paramref name="userId"/> at <paramref name="businessId"/>; only a completed one can be reviewed.</summary>
    public BookingReference SeedBooking(Guid userId, Guid businessId, bool completed = true)
    {
        var booking = new BookingReference { Id = Guid.NewGuid(), UserId = userId, BusinessId = businessId };
        _bookings[booking.Id] = booking;
        if (completed)
            _completed.Add(booking.Id);
        return booking;
    }

    /// <summary>Seeds a review directly, bypassing CreateAsync — for test setup.</summary>
    public Review SeedReview(Guid businessId, short rating, DateTimeOffset createdAt, string? comment = null)
    {
        var review = new Review
        {
            Id = Guid.NewGuid(),
            BookingId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            BusinessId = businessId,
            Rating = rating,
            Comment = comment,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
        _reviews[review.Id] = review;
        return review;
    }

    public Task<BookingReference?> GetBookingAsync(Guid bookingId, CancellationToken ct = default) =>
        Task.FromResult(_bookings.GetValueOrDefault(bookingId));

    public Task CreateAsync(Review review, CancellationToken ct = default)
    {
        if (!_bookings.TryGetValue(review.BookingId, out var booking) || !_completed.Contains(booking.Id)
            || booking.UserId != review.UserId || booking.BusinessId != review.BusinessId)
            throw new BookingNotReviewableException(review.BookingId);

        if (_reviews.Values.Any(r => r.BookingId == review.BookingId))
            throw new ReviewAlreadyExistsException(review.BookingId);

        // The database defaults (gen_random_uuid(), CURRENT_TIMESTAMP).
        review.Id = Guid.NewGuid();
        review.CreatedAt = DateTimeOffset.UtcNow;
        review.UpdatedAt = review.CreatedAt;
        _reviews[review.Id] = review;
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<Review> Items, int Total)> ListByBusinessAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _reviews.Values
            .Where(r => r.BusinessId == businessId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToList();

        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(((IReadOnlyList<Review>)pageItems, all.Count));
    }
}
