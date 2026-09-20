namespace Spot.Business.Api.DTOs;

/// <summary>The three ways <c>GET /business/categories?parentCategoryId=...</c> can be filtered.</summary>
public enum ParentCategoryFilter
{
    /// <summary>No <c>parentCategoryId</c> query parameter — every category, at any level.</summary>
    Any,

    /// <summary><c>parentCategoryId=null</c> — root categories only (<c>ParentCategoryId IS NULL</c>).</summary>
    RootOnly,

    /// <summary><c>parentCategoryId={id}</c> — that category's direct subcategories only.</summary>
    Specific,
}
