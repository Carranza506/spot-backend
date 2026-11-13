using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Booking" tag, favorite businesses (#71). The contract puts these under
/// /booking, but favorite_businesses and businesses are both owned by Spot.Business.Api, so the
/// Gateway routes /api/v1/booking/favorites/** here (see Spot.Gateway's "booking-favorites-route").
/// Every action is CLIENT-only and works on the caller's own favorites (JWT sub) — never on
/// another user's.
/// </summary>
[ApiController]
[Route("booking/favorites")]
[Authorize(Roles = "CLIENT")]
public class FavoritesController(IBusinessService businessService) : ControllerBase
{
    /// <summary>GET /booking/favorites: newest first; favorites of an inactive business are left out.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<FavoriteBusinessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListFavoriteBusinesses(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
            return InvalidToken();

        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        return Ok(await businessService.ListFavoritesAsync(userId, page, pageSize, ct));
    }

    /// <summary>
    /// PUT /booking/favorites/{businessId}: idempotent — 204 whether it's a new favorite or already
    /// one. 404 if the business doesn't exist or is inactive, even if it's already a favorite.
    /// </summary>
    [HttpPut("{businessId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFavoriteBusiness(Guid businessId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return InvalidToken();

        return await businessService.AddFavoriteAsync(userId, businessId, ct)
            ? NoContent()
            : NotFound(new ApiError("NOT_FOUND", "El negocio indicado no existe."));
    }

    /// <summary>
    /// DELETE /booking/favorites/{businessId}: idempotent — always 204, whether or not it was a
    /// favorite and whether or not the business exists or is active. Hard delete of the caller's row only.
    /// </summary>
    [HttpDelete("{businessId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveFavoriteBusiness(Guid businessId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return InvalidToken();

        await businessService.RemoveFavoriteAsync(userId, businessId, ct);
        return NoContent();
    }

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name is the caller's account id (JWT sub).</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
