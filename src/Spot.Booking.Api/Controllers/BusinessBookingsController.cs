using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Booking.Api.Controllers;

/// <summary>contracts/spot-api.yaml, "Booking" tag: a business's bookings (#68).</summary>
[ApiController]
[Route("booking/businesses/{businessId:guid}/bookings")]
public class BusinessBookingsController(IBookingService bookingService) : ControllerBase
{
    /// <summary>
    /// GET /booking/businesses/{businessId}/bookings (operationId listBusinessBookings): requires the
    /// BUSINESS account that owns the business (other roles get 403 from the role check, other
    /// BUSINESS accounts from the ownership check). Earliest startAt first. status filters by
    /// status; from/to are optional, inclusive, YYYY-MM-DD dates in the business's time zone
    /// (America/Costa_Rica), matched against the booking's start.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(PaginatedResponse<BookingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListBusinessBookings(
        Guid businessId, [FromQuery] BusinessBookingListQuery query, CancellationToken ct)
    {
        if (!Guid.TryParse(User.Identity?.Name, out var callerId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        if (!BookingListQuery.TryParseStatus(query.Status, out var status))
            return BookingsController.InvalidStatus();

        if (!TryParseDate(query.From, out var from) || !TryParseDate(query.To, out var to))
            return BadRequest(new ApiError("BAD_REQUEST", "from y to deben tener el formato YYYY-MM-DD."));

        if (from > to)
            return BadRequest(new ApiError("BAD_REQUEST", "from no puede ser posterior a to."));

        try
        {
            return Ok(await bookingService.ListBusinessAsync(
                businessId, callerId, status, from, to, query.Page, query.PageSize, ct));
        }
        catch (BusinessNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));
        }
        catch (BookingAccessDeniedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));
        }
    }

    /// <summary>An absent value is valid (no bound); a present one must be exactly YYYY-MM-DD and a real date.</summary>
    private static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null)
            return true;

        if (!DateOnly.TryParseExact(
                value, BusinessBookingListQuery.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        date = parsed;
        return true;
    }
}
