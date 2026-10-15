using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Controllers;

[ApiController]
[Route("ai-search/requests")]
[Authorize(Roles = "SUPERADMIN")]
public class AiRequestsController(IAiRequestService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<AiRequestLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAiRequests(
        [FromQuery] AiSearchFilterQuery filter,
        CancellationToken ct)
    {
        if (filter.Page < 1 || filter.PageSize < 1 || filter.PageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        var result = await service.ListAsync(filter, ct);
        return Ok(result);
    }

    // The :guid constraint answers a malformed id with 404 (no matching route): the contract
    // documents no 400 for this endpoint.
    [HttpGet("{aiRequestId:guid}")]
    [ProducesResponseType(typeof(AiRequestLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAiRequest(Guid aiRequestId, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(aiRequestId, ct);
        return result is null
            ? NotFound(new ApiError("NOT_FOUND", "La interacción de IA indicada no existe."))
            : Ok(result);
    }
}
