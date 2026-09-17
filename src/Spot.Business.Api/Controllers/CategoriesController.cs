using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, category endpoints. <c>GET /business/categories</c>
/// (#55) is implemented so far — create/update/delete are separate, not-yet-implemented steps
/// under the same issue. Listing is public, so there's no class-level [Authorize]: the
/// SUPERADMIN-only actions that come later apply it per-action instead, same as AuthController.
/// </summary>
[ApiController]
[Route("business/categories")]
public class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>
    /// GET /business/categories: public — no access token required (contract: security: []).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListCategories([FromQuery] CategoryFilterQuery filter, CancellationToken ct)
    {
        if (filter.Page < 1 || filter.PageSize < 1 || filter.PageSize > 100)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        if (!TryParseParentFilter(filter.ParentCategoryId, out var parentFilter, out var parentCategoryId))
        {
            return BadRequest(new ApiError(
                "BAD_REQUEST", "parentCategoryId debe ser \"null\" o el id de una categoría existente."));
        }

        var result = await categoryService.ListAsync(filter.Page, filter.PageSize, parentFilter, parentCategoryId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Parses the raw <c>parentCategoryId</c> query value into its three possible meanings —
    /// see <see cref="ParentCategoryFilter"/> and <see cref="CategoryFilterQuery"/>.
    /// </summary>
    private static bool TryParseParentFilter(string? raw, out ParentCategoryFilter filter, out Guid? parentCategoryId)
    {
        parentCategoryId = null;

        if (raw is null)
        {
            filter = ParentCategoryFilter.Any;
            return true;
        }

        if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
        {
            filter = ParentCategoryFilter.RootOnly;
            return true;
        }

        if (Guid.TryParse(raw, out var id))
        {
            filter = ParentCategoryFilter.Specific;
            parentCategoryId = id;
            return true;
        }

        filter = default;
        return false;
    }
}
