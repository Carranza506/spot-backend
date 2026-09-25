using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, business CRUD endpoints (#56). A BUSINESS account IS
/// the business (businesses.account_id == JWT sub, since #107). GET /{businessId} is public, so
/// [Authorize] is applied per-action, same as CategoriesController. The /{businessId} routes are
/// constrained to :guid, so "me" can never be captured as an id.
/// </summary>
[ApiController]
[Route("business/businesses")]
public class BusinessesController(IBusinessService businessService) : ControllerBase
{
    /// <summary>POST /business/businesses: requires BUSINESS. The account is always the caller's own JWT sub, never taken from the body.</summary>
    [HttpPost]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBusiness([FromBody] BusinessCreateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var accountId))
            return InvalidToken();

        try
        {
            var result = await businessService.CreateAsync(accountId, request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (BusinessAlreadyExistsException)
        {
            return Conflict(new ApiError("BUSINESS_ALREADY_EXISTS", "Esta cuenta ya tiene un negocio registrado."));
        }
    }

    /// <summary>GET /business/businesses/me: requires BUSINESS. Returns the caller's business even if it's inactive.</summary>
    [HttpGet("me")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOwnBusiness(CancellationToken ct)
    {
        if (!TryGetUserId(out var accountId))
            return InvalidToken();

        var result = await businessService.GetOwnAsync(accountId, ct);
        return result is null ? OwnBusinessNotFound() : Ok(result);
    }

    /// <summary>PATCH /business/businesses/me: requires BUSINESS. Works on an inactive business too (e.g. to reactivate it).</summary>
    [HttpPatch("me")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOwnBusiness([FromBody] BusinessUpdateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var accountId))
            return InvalidToken();

        var result = await businessService.UpdateOwnAsync(accountId, request, ct);
        return result is null ? OwnBusinessNotFound() : Ok(result);
    }

    /// <summary>GET /business/businesses/{businessId}: public (contract: security: []). An inactive business is reported as 404.</summary>
    [HttpGet("{businessId:guid}")]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBusiness(Guid businessId, CancellationToken ct)
    {
        var result = await businessService.GetPublicAsync(businessId, ct);
        return result is null ? BusinessNotFound() : Ok(result);
    }

    /// <summary>PATCH /business/businesses/{businessId}: requires the BUSINESS account that owns it (SUPERADMIN included in the 403).</summary>
    [HttpPatch("{businessId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBusiness(Guid businessId, [FromBody] BusinessUpdateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var result = await businessService.UpdateAsync(businessId, callerId, request, ct);
            return result is null ? BusinessNotFound() : Ok(result);
        }
        catch (BusinessAccessDeniedException)
        {
            return NotOwner();
        }
    }

    /// <summary>
    /// DELETE /business/businesses/{businessId}: requires the owning BUSINESS account. Soft delete
    /// (is_active = false) — the row has bookings/reviews attached. Idempotent on an inactive business.
    /// </summary>
    [HttpDelete("{businessId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateBusiness(Guid businessId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var deactivated = await businessService.DeactivateAsync(businessId, callerId, ct);
            return deactivated ? NoContent() : BusinessNotFound();
        }
        catch (BusinessAccessDeniedException)
        {
            return NotOwner();
        }
    }

    private NotFoundObjectResult BusinessNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));

    private NotFoundObjectResult OwnBusinessNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "Esta cuenta todavía no tiene un negocio registrado."));

    private ObjectResult NotOwner() =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>
    /// Same as AuthController.TryGetUserId: AddSpotJwtAuthentication sets NameClaimType = "sub",
    /// so User.Identity.Name IS the caller's account id. This only guards against a sub that
    /// isn't a Guid, which a token issued by Spot.Auth.Api never has.
    /// </summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
