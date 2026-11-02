using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public interface IReviewService
{
    /// <summary>The booking's client reviews it; the business is the booking's.</summary>
    /// <exception cref="BookingNotFoundException">The booking doesn't exist.</exception>
    /// <exception cref="BookingAccessDeniedException"><paramref name="callerId"/> isn't the booking's client.</exception>
    /// <exception cref="BookingNotReviewableException">The booking isn't COMPLETED (trg_validate_review).</exception>
    /// <exception cref="ReviewAlreadyExistsException">The booking already has a review.</exception>
    Task<ReviewDto> CreateAsync(Guid bookingId, Guid callerId, ReviewCreateRequest request, CancellationToken ct = default);

    /// <summary>Public listing, newest first: null if the business doesn't exist or is inactive (same visibility as GET /business/businesses/{businessId}).</summary>
    Task<PaginatedResponse<ReviewDto>?> ListAsync(Guid businessId, int page, int pageSize, CancellationToken ct = default);
}
