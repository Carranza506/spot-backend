using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, business contact endpoints (#59). Listing is public,
/// so [Authorize] is applied per-action, same as BusinessesController. Writes require the BUSINESS
/// account that owns the business (#56's ownership check).
/// </summary>
[ApiController]
[Route("business/businesses/{businessId:guid}/contacts")]
public class BusinessContactsController(IBusinessContactService contactService) : ControllerBase
{
    /// <summary>
    /// GET /business/businesses/{businessId}/contacts: public (contract: security: []). An
    /// inactive business is reported as 404, same as GET /business/businesses/{businessId}.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<BusinessContactDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListBusinessContacts(
        Guid businessId, [FromQuery] BusinessContactListQuery query, CancellationToken ct)
    {
        // Not a documented response of listBusinessContacts, but the same guard every other
        // listing endpoint applies (see CategoriesController) — agreed for #59.
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        var result = await contactService.ListAsync(businessId, query.Page, query.PageSize, ct);
        return result is null ? BusinessNotFound() : Ok(result);
    }

    /// <summary>POST /business/businesses/{businessId}/contacts: requires the BUSINESS account that owns the business.</summary>
    [HttpPost]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BusinessContactDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateBusinessContact(
        Guid businessId, [FromBody] BusinessContactCreateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var result = await contactService.CreateAsync(businessId, callerId, request, ct);
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
    }

    /// <summary>
    /// DELETE /business/businesses/{businessId}/contacts/{contactId}: requires the BUSINESS account
    /// that owns the business. Hard delete. A contact of a different business is reported as 404.
    /// </summary>
    [HttpDelete("{contactId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBusinessContact(Guid businessId, Guid contactId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var deleted = await contactService.DeleteAsync(businessId, contactId, callerId, ct);
            return deleted
                ? NoContent()
                : NotFound(new ApiError("NOT_FOUND", "El contacto indicado no existe."));
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

    private NotFoundObjectResult BusinessNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));

    private ObjectResult NotOwner() =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name is the JWT sub.</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
