using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, schedule exception endpoints (#61). Listing is public,
/// so [Authorize] is applied per-action, same as BusinessContactsController. Writes require the
/// BUSINESS account that owns the business (#56's ownership check), even if it's inactive.
/// </summary>
[ApiController]
[Route("business/businesses/{businessId:guid}/schedule-exceptions")]
public class BusinessScheduleExceptionsController(IBusinessScheduleExceptionService exceptionService) : ControllerBase
{
    /// <summary>
    /// GET /business/businesses/{businessId}/schedule-exceptions: public (contract: security: []).
    /// Ordered by date ascending; from/to are optional, inclusive and must be YYYY-MM-DD. An inactive
    /// business is reported as 404, same as GET /business/businesses/{businessId}.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<BusinessScheduleExceptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListBusinessScheduleExceptions(
        Guid businessId, [FromQuery] BusinessScheduleExceptionListQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        if (!TryParseDate(query.From, out var from) || !TryParseDate(query.To, out var to))
            return BadRequest(new ApiError("BAD_REQUEST", "from y to deben tener el formato YYYY-MM-DD."));

        if (from > to)
            return BadRequest(new ApiError("BAD_REQUEST", "from no puede ser posterior a to."));

        var result = await exceptionService.ListAsync(businessId, from, to, query.Page, query.PageSize, ct);
        return result is null ? BusinessNotFound() : Ok(result);
    }

    /// <summary>
    /// POST /business/businesses/{businessId}/schedule-exceptions: requires the BUSINESS account that
    /// owns the business. One exception per date; a closed day stores null times.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BusinessScheduleExceptionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateBusinessScheduleException(
        Guid businessId, [FromBody] BusinessScheduleExceptionCreateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var result = await exceptionService.CreateAsync(businessId, callerId, request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (BusinessNotFoundException)
        {
            return BusinessNotFound();
        }
        catch (BusinessAccessDeniedException)
        {
            return NotOwner();
        }
        catch (InvalidBusinessHoursException ex)
        {
            return UnprocessableEntity(new ApiError(ex.Code, ex.Message));
        }
        catch (ScheduleExceptionAlreadyExistsException)
        {
            return Conflict(new ApiError(
                "SCHEDULE_EXCEPTION_ALREADY_EXISTS", "El negocio ya tiene una excepción de horario para esa fecha."));
        }
    }

    /// <summary>
    /// DELETE /business/businesses/{businessId}/schedule-exceptions/{exceptionId}: requires the
    /// BUSINESS account that owns the business. Hard delete. An exception of a different business
    /// is reported as 404.
    /// </summary>
    [HttpDelete("{exceptionId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBusinessScheduleException(Guid businessId, Guid exceptionId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var deleted = await exceptionService.DeleteAsync(businessId, exceptionId, callerId, ct);
            return deleted
                ? NoContent()
                : NotFound(new ApiError("NOT_FOUND", "La excepción de horario indicada no existe."));
        }
        catch (BusinessNotFoundException)
        {
            return BusinessNotFound();
        }
        catch (BusinessAccessDeniedException)
        {
            return NotOwner();
        }
    }

    /// <summary>An absent value is valid (no bound); a present one must be exactly YYYY-MM-DD and a real date.</summary>
    private static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null)
            return true;

        if (!DateOnly.TryParseExact(
                value, BusinessScheduleExceptionListQuery.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        date = parsed;
        return true;
    }

    private NotFoundObjectResult BusinessNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));

    private ObjectResult NotOwner() =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name is the JWT sub.</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
