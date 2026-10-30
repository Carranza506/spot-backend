using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public sealed class ReviewService(
    IBusinessRepository businessRepository,
    IReviewRepository reviewRepository) : IReviewService
{
    /// <summary>
    /// 404, then 403, then the database's rules (422, 409): whether the booking is COMPLETED or
    /// already reviewed is only reached by its own client, so nobody else can learn it.
    /// </summary>
    public async Task<ReviewDto> CreateAsync(
        Guid bookingId, Guid callerId, ReviewCreateRequest request, CancellationToken ct = default)
    {
        var booking = await reviewRepository.GetBookingAsync(bookingId, ct)
                      ?? throw new BookingNotFoundException(bookingId);
        if (booking.UserId != callerId)
            throw new BookingAccessDeniedException(bookingId, callerId);

        var review = new Review
        {
            BookingId = bookingId,
            UserId = callerId,
            BusinessId = booking.BusinessId,
            Rating = (short)request.Rating!.Value,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
        };
        await reviewRepository.CreateAsync(review, ct);

        return ReviewDto.FromEntity(review);
    }

    public async Task<PaginatedResponse<ReviewDto>?> ListAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var business = await businessRepository.GetByIdAsync(businessId, ct);
        if (business is not { IsActive: true })
            return null;

        var (items, total) = await reviewRepository.ListByBusinessAsync(businessId, page, pageSize, ct);
        var dtos = items.Select(ReviewDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new PaginatedResponse<ReviewDto>(dtos, new PaginationMeta(page, pageSize, total, totalPages));
    }
}
