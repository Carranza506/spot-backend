using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, category endpoints — full CRUD for #55. Listing is
/// public, so there's no class-level [Authorize]: the SUPERADMIN-only actions apply it per-action
/// instead, same as AuthController.
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

    /// <summary>POST /business/categories: requires SUPERADMIN — managing the global category catalog is an administrative task.</summary>
    [HttpPost]
    [Authorize(Roles = "SUPERADMIN")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryCreateRequest request, CancellationToken ct)
    {
        try
        {
            var result = await categoryService.CreateAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (CategoryNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "La categoría padre indicada no existe."));
        }
        catch (InvalidCategoryHierarchyException ex)
        {
            return BadRequest(new ApiError("BAD_REQUEST", ex.Message));
        }
        catch (DuplicateCategoryException)
        {
            return Conflict(new ApiError("CATEGORY_ALREADY_EXISTS", "Ya existe una categoría con ese nombre bajo el mismo padre."));
        }
    }

    /// <summary>PATCH /business/categories/{categoryId}: requires SUPERADMIN.</summary>
    [HttpPatch("{categoryId}")]
    [Authorize(Roles = "SUPERADMIN")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCategory(Guid categoryId, [FromBody] CategoryUpdateRequest request, CancellationToken ct)
    {
        try
        {
            var result = await categoryService.UpdateAsync(categoryId, request, ct);
            if (result is null)
                return NotFound(new ApiError("NOT_FOUND", "La categoría indicada no existe."));

            return Ok(result);
        }
        catch (CategoryNotFoundException)
        {
            return NotFound(new ApiError("NOT_FOUND", "La categoría padre indicada no existe."));
        }
        catch (InvalidCategoryHierarchyException ex)
        {
            return BadRequest(new ApiError("BAD_REQUEST", ex.Message));
        }
        catch (DuplicateCategoryException)
        {
            return Conflict(new ApiError("CATEGORY_ALREADY_EXISTS", "Ya existe una categoría con ese nombre bajo el mismo padre."));
        }
    }

    /// <summary>
    /// DELETE /business/categories/{categoryId}: requires SUPERADMIN. Never cascades — a
    /// category with subcategories cannot be deleted (remove/reparent them first).
    /// </summary>
    [HttpDelete("{categoryId}")]
    [Authorize(Roles = "SUPERADMIN")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(Guid categoryId, CancellationToken ct)
    {
        try
        {
            var deleted = await categoryService.DeleteAsync(categoryId, ct);
            if (!deleted)
                return NotFound(new ApiError("NOT_FOUND", "La categoría indicada no existe."));

            return NoContent();
        }
        catch (CategoryHasSubcategoriesException)
        {
            return Conflict(new ApiError(
                "CATEGORY_HAS_SUBCATEGORIES", "No se puede eliminar una categoría que tiene subcategorías."));
        }
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
