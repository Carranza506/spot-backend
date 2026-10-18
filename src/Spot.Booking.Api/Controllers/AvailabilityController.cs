using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Booking.Api.Controllers;

/// <summary>contracts/spot-api.yaml, "Booking" tag: business availability (#66). Public (security: []).</summary>
[ApiController]
// No :guid route constraint on purpose: a malformed businessId must fail model binding (400, Error
// shape), not fall through to a 404 for an unmatched route.
[Route("booking/businesses/{businessId}/availability")]
public class AvailabilityController(IAvailabilityService availabilityService) : ControllerBase
{
    /// <summary>
    /// GET /booking/businesses/{businessId}/availability (operationId getBusinessAvailability).
    /// from/to are required, inclusive, YYYY-MM-DD and at most 31 days apart; slotMinutes is
    /// validated against 5–480 whenever it's sent, even if serviceId makes it unused. 404 if the
    /// business doesn't exist or is inactive, or the service doesn't exist, is inactive or belongs
    /// to another business.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBusinessAvailability(
        Guid businessId, [FromQuery] AvailabilityQuery query, CancellationToken ct)
    {
        if (!TryParseDate(query.From, out var from))
            return InvalidParameter("from", "from es requerido y debe tener el formato YYYY-MM-DD.");

        if (!TryParseDate(query.To, out var to))
            return InvalidParameter("to", "to es requerido y debe tener el formato YYYY-MM-DD.");

        if (to < from)
            return InvalidParameter("to", "to no puede ser anterior a from.");

        if (to.DayNumber - from.DayNumber + 1 > AvailabilityQuery.MaxRangeDays)
            return InvalidParameter("to", $"El rango de fechas no puede superar {AvailabilityQuery.MaxRangeDays} días.");

        var slotMinutes = query.SlotMinutes ?? AvailabilityQuery.DefaultSlotMinutes;
        if (slotMinutes is < AvailabilityQuery.MinSlotMinutes or > AvailabilityQuery.MaxSlotMinutes)
            return InvalidParameter(
                "slotMinutes",
                $"slotMinutes debe estar entre {AvailabilityQuery.MinSlotMinutes} y {AvailabilityQuery.MaxSlotMinutes}.");

        try
        {
            var result = await availabilityService.GetAvailabilityAsync(businessId, from, to, query.ServiceId, slotMinutes, ct);
            return Ok(result);
        }
        catch (BusinessNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));
        }
        catch (ServiceNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "El servicio indicado no existe."));
        }
    }

    /// <summary>
    /// Required, exactly YYYY-MM-DD and a real date. 9999-12-31 is rejected too: the day after it,
    /// needed to bound the range in UTC, isn't representable.
    /// </summary>
    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, AvailabilityQuery.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        && date < DateOnly.MaxValue;

    private BadRequestObjectResult InvalidParameter(string field, string message) =>
        BadRequest(new ApiError("BAD_REQUEST", message, new { field }));
}
