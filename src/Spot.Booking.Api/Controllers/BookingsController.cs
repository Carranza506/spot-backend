using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Booking.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Booking" tag: bookings (#67, #68) and their status transitions (#69).
/// A business's bookings are listed by BusinessBookingsController, since they live under
/// /booking/businesses/{businessId}.
/// </summary>
[ApiController]
[Route("booking/bookings")]
public class BookingsController(IBookingService bookingService) : ControllerBase
{
    /// <summary>
    /// GET /booking/bookings (operationId listOwnBookings): any authenticated role; only the
    /// caller's own bookings (bookings.user_id = JWT sub), most recent first, optionally by status.
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PaginatedResponse<BookingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListOwnBookings([FromQuery] BookingListQuery query, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return InvalidToken();

        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        if (!BookingListQuery.TryParseStatus(query.Status, out var status))
            return InvalidStatus();

        return Ok(await bookingService.ListOwnAsync(userId, status, query.Page, query.PageSize, ct));
    }

    /// <summary>
    /// GET /booking/bookings/{bookingId} (operationId getBooking): the booking's client, the
    /// BUSINESS account that owns its business, or a SUPERADMIN; anyone else gets 403. A booking
    /// that doesn't exist is 404 for every caller.
    /// </summary>
    [HttpGet("{bookingId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBooking(Guid bookingId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            return Ok(await bookingService.GetAsync(bookingId, callerId, User.IsInRole("SUPERADMIN"), ct));
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
    }

    /// <summary>
    /// POST /booking/bookings (operationId createBooking): requires a CLIENT token; the booking is
    /// the caller's (JWT sub). 404 if the business or service can't be booked, 422 if startAt is in
    /// the past or the booking falls outside opening hours, 409 if it overlaps an active booking.
    /// No Location header: no other service's 201 sets one.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "CLIENT")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateBooking([FromBody] BookingCreateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return InvalidToken();

        try
        {
            var result = await bookingService.CreateAsync(userId, request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (BusinessNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));
        }
        catch (ServiceNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "El servicio indicado no existe."));
        }
        catch (BookingRuleViolationException ex)
        {
            return UnprocessableEntity(new ApiError(ex.Code, ex.Message));
        }
        catch (BookingSlotOverlapException)
        {
            return Conflict(new ApiError("BOOKING_SLOT_OVERLAP", "Ese horario ya no está disponible para este negocio."));
        }
    }

    /// <summary>
    /// POST /booking/bookings/{bookingId}/cancel (operationId cancelBooking): the booking's CLIENT
    /// or the BUSINESS that owns it; only from PENDING or CONFIRMED, else 422. The optional reason
    /// isn't read: bookings has no column for it and the Booking schema doesn't return it.
    /// </summary>
    [HttpPost("{bookingId:guid}/cancel")]
    [Authorize(Roles = "CLIENT,BUSINESS")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> CancelBooking(Guid bookingId, CancellationToken ct) =>
        ChangeStatus(callerId => bookingService.CancelAsync(bookingId, callerId, ct));

    /// <summary>
    /// POST /booking/bookings/{bookingId}/complete (operationId completeBooking): the BUSINESS that
    /// owns the booking; only from CONFIRMED, else 422.
    /// </summary>
    [HttpPost("{bookingId:guid}/complete")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> CompleteBooking(Guid bookingId, CancellationToken ct) =>
        ChangeStatus(callerId => bookingService.CompleteAsync(bookingId, callerId, ct));

    /// <summary>
    /// POST /booking/bookings/{bookingId}/no-show (operationId markBookingNoShow): the BUSINESS that
    /// owns the booking; only from CONFIRMED, else 422.
    /// </summary>
    [HttpPost("{bookingId:guid}/no-show")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> MarkBookingNoShow(Guid bookingId, CancellationToken ct) =>
        ChangeStatus(callerId => bookingService.MarkNoShowAsync(bookingId, callerId, ct));

    /// <summary>The error mapping the three status transitions share.</summary>
    private async Task<IActionResult> ChangeStatus(Func<Guid, Task<BookingDto>> transition)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            return Ok(await transition(callerId));
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
        catch (BookingRuleViolationException ex)
        {
            return UnprocessableEntity(new ApiError(ex.Code, ex.Message));
        }
    }

    internal static BadRequestObjectResult InvalidStatus() =>
        new(new ApiError("BAD_REQUEST", "status debe ser PENDING, CONFIRMED, COMPLETED, CANCELLED o NO_SHOW."));

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>Same as Spot.Business.Api's controllers: User.Identity.Name IS the caller's account id (JWT sub).</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
