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
    /// GET /business/businesses/{businessId}/services: public (contract: security: []). Paginated,
    /// active-only by default (isActive filters). 404 if the business doesn't exist or is inactive.
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

        var result = await serviceOfferingService.ListAsync(businessId, page, pageSize, isActive, ct);
        return result is null ? BusinessNotFound() : Ok(result);
    }

    /// <summary>
    /// GET /business/services/{serviceId}: public (contract: security: []). 404 if the service
    /// doesn't exist, is inactive, or its business is inactive.
    /// </summary>
    [HttpGet("services/{serviceId:guid}")]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetService(Guid serviceId, CancellationToken ct)
    {
        var result = await serviceOfferingService.GetAsync(serviceId, ct);
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
}
