using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Booking" tag, reviews (#70). The contract puts these under /booking,
/// but reviews is owned by Spot.Business.Api (same as favorites), so the Gateway routes both paths
/// here (see Spot.Gateway's "booking-review-route" and "booking-business-reviews-route").
/// </summary>
[ApiController]
[Route("booking")]
public class ReviewsController(IReviewService reviewService) : ControllerBase
{
    /// <summary>
    /// POST /booking/bookings/{bookingId}/review (operationId createBookingReview): the CLIENT who
    /// owns the booking, once, and only for a COMPLETED booking.
    /// </summary>
    [HttpPost("bookings/{bookingId:guid}/review")]
    [Authorize(Roles = "CLIENT")]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateBookingReview(
        Guid bookingId, [FromBody] ReviewCreateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var review = await reviewService.CreateAsync(bookingId, callerId, request, ct);
            return StatusCode(StatusCodes.Status201Created, review);
        }
        catch (BookingNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "La reservación indicada no existe."));
        }
        catch (BookingAccessDeniedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));
        }
        catch (ReviewAlreadyExistsException)
        {
            return Conflict(new ApiError("REVIEW_ALREADY_EXISTS", "Esta reservación ya tiene una reseña."));
        }
        catch (BookingNotReviewableException)
        {
            // The contract defines no code for this case; same convention as BOOKING_START_IN_PAST.
            return UnprocessableEntity(new ApiError("REVIEW_BOOKING_NOT_COMPLETED", "Solo se puede reseñar una reservación completada."));
        }
    }

    /// <summary>
    /// GET /booking/businesses/{businessId}/reviews (operationId listBusinessReviews): public,
    /// newest first. An inactive business is a 404, same as GET /business/businesses/{businessId}.
    /// </summary>
    [HttpGet("businesses/{businessId:guid}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PaginatedResponse<ReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListBusinessReviews(
        Guid businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        // Not a documented response of listBusinessReviews, but the same guard every other listing applies.
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        var result = await reviewService.ListAsync(businessId, page, pageSize, ct);
        return result is null
            ? NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."))
            : Ok(result);
    }

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name is the caller's account id (JWT sub).</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
