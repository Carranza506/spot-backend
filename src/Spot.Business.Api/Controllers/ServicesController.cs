using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, "BUSINESS - Services" endpoints (#63, split into
/// #113-#116). The contract spreads them over two bases — /business/businesses/{businessId}/services
/// and /business/services/{serviceId} — so the class route is just "business" and each action
/// carries the rest. Listing/detail are public, so [Authorize] is applied per-action, same as
/// BusinessesController.
/// </summary>
[ApiController]
[Route("business")]
public class ServicesController(IServiceOfferingService serviceOfferingService) : ControllerBase
{
    /// <summary>
    /// POST /business/businesses/{businessId}/services: requires the BUSINESS account that owns the
    /// business. The business always comes from the route and the caller from the JWT sub, never
    /// from the body.
    /// </summary>
    [HttpPost("businesses/{businessId:guid}/services")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateService(
        Guid businessId, [FromBody] ServiceCreateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var result = await serviceOfferingService.CreateAsync(businessId, callerId, request, ct);
            return result is null ? BusinessNotFound() : StatusCode(StatusCodes.Status201Created, result);
        }
        catch (BusinessAccessDeniedException)
        {
            return NotOwner();
        }
    }

    /// <summary>
    /// PATCH /business/services/{serviceId}: requires the BUSINESS account that owns the service's
    /// business. The route has no businessId, so ownership is resolved through the service itself.
    /// 404 if the service doesn't exist, 403 if the caller isn't its owner.
    /// </summary>
    [HttpPatch("services/{serviceId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateService(
        Guid serviceId, [FromBody] ServiceUpdateRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var result = await serviceOfferingService.UpdateAsync(serviceId, callerId, request, ct);
            return result is null ? ServiceNotFound() : Ok(result);
        }
        catch (BusinessAccessDeniedException)
        {
            return NotOwner();
        }
    }

    /// <summary>
    /// DELETE /business/services/{serviceId}: requires the BUSINESS account that owns the service's
    /// business. Soft delete (is_active = false) — never a physical delete, to keep booking history.
    /// Idempotent: deactivating an already inactive service still returns 204. 404 if it doesn't exist.
    /// </summary>
    [HttpDelete("services/{serviceId:guid}")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateService(Guid serviceId, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            var deactivated = await serviceOfferingService.DeactivateAsync(serviceId, callerId, ct);
            return deactivated ? NoContent() : ServiceNotFound();
        }
        catch (BusinessAccessDeniedException)
        {
            return NotOwner();
        }
    }

    /// <summary>
    /// GET /business/businesses/{businessId}/services: public, but owner-aware. Anonymous/non-owner
    /// callers see only active services; the owner (a BUSINESS token owning the business) sees all
    /// and can filter with isActive. 404 if the business doesn't exist, or is inactive for non-owners.
    /// No [Authorize]: a token is optional and only widens what the caller sees.
    /// </summary>
    [HttpGet("businesses/{businessId:guid}/services")]
    [ProducesResponseType(typeof(PaginatedResponse<ServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListBusinessServices(
        Guid businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        var result = await serviceOfferingService.ListAsync(businessId, page, pageSize, isActive, CallerIdOrNull(), ct);
        return result is null ? BusinessNotFound() : Ok(result);
    }

    /// <summary>
    /// GET /business/services/{serviceId}: public, but owner-aware. The owner sees their own service
    /// even if inactive; everyone else gets 404 for an inactive service or a service of an inactive
    /// business. A token is optional.
    /// </summary>
    [HttpGet("services/{serviceId:guid}")]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetService(Guid serviceId, CancellationToken ct)
    {
        var result = await serviceOfferingService.GetAsync(serviceId, CallerIdOrNull(), ct);
        return result is null ? ServiceNotFound() : Ok(result);
    }

    private NotFoundObjectResult BusinessNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));

    private NotFoundObjectResult ServiceNotFound() =>
        NotFound(new ApiError("NOT_FOUND", "El servicio indicado no existe."));

    private ObjectResult NotOwner() =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name IS the caller's account id (JWT sub).</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);

    /// <summary>The caller's account id when a valid token was sent, or null for anonymous callers — for the owner-aware public reads.</summary>
    private Guid? CallerIdOrNull() => TryGetUserId(out var id) ? id : null;
}
