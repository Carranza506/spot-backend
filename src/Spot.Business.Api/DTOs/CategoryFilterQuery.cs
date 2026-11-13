namespace Spot.Business.Api.DTOs;

/// <summary>
/// Raw query parameters for <c>GET /business/categories</c>, bound as-is by model binding.
/// <see cref="ParentCategoryId"/> is deliberately a string, not a <c>Guid?</c>: the contract
/// gives it three distinct meanings — absent (no filter), the literal <c>"null"</c> (root
/// categories only), or an actual category id (that category's direct subcategories) — and
/// binding straight to <c>Guid?</c> would make ASP.NET Core reject <c>"null"</c> as an invalid
/// guid before this code ever saw it. See <see cref="Controllers.CategoriesController"/> for how
/// this gets parsed into a <see cref="ParentCategoryFilter"/>.
/// </summary>
public sealed class CategoryFilterQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? ParentCategoryId { get; set; }
}
