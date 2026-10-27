using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Services;
using Spot.AiSearch.Api.Services.Business;
using Spot.Shared.Errors;

namespace Spot.AiSearch.Api.Controllers;

/// <summary>contracts/spot-api.yaml, "AiSearch" tag: natural language business search (#73).</summary>
[ApiController]
[Route("ai-search")]
[Authorize]
public class AiSearchController(IAiSearchQueryService service) : ControllerBase
{
    /// <summary>
    /// POST /ai-search/query (operationId aiSearchQuery): any authenticated user (CLIENT, BUSINESS,
    /// SUPERADMIN). 422 when nothing usable could be extracted; a failing AI model or Business is a
    /// generic 500, never with its details. The call is stored in ai_requests in every case.
    /// </summary>
    [HttpPost("query")]
    [ProducesResponseType(typeof(AiSearchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AiSearchQuery([FromBody] AiSearchQueryRequest request, CancellationToken ct)
    {
        // [StringLength] counts whitespace, so a blank or padded-out query passes it: checked trimmed here.
        if (request.Query!.Trim().Length < AiSearchQueryRequest.QueryMinLength)
            return BadRequest(new ApiError(
                "BAD_REQUEST",
                $"La búsqueda debe tener al menos {AiSearchQueryRequest.QueryMinLength} caracteres.",
                new { field = "query" }));

        var userAgent = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await service.SearchAsync(
                request,
                Guid.TryParse(User.Identity?.Name, out var userId) ? userId : null,
                HttpContext.Connection.RemoteIpAddress,
                string.IsNullOrEmpty(userAgent) ? null : userAgent,
                ct);
            return Ok(result);
        }
        catch (AiSearchNotUnderstoodException)
        {
            return UnprocessableEntity(new ApiError(
                AiSearchQueryService.NotUnderstoodErrorCode,
                "No pudimos entender tu búsqueda. Intenta describir el servicio que buscas."));
        }
        catch (Exception ex) when (ex is AiSearchUnavailableException or BusinessSearchUnavailableException)
        {
            // Same body as Program.cs's exception handler: what failed stays in the logs and ai_requests.
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiError("INTERNAL_ERROR", "Ocurrió un error inesperado. Por favor intenta de nuevo más tarde."));
        }
    }
}
