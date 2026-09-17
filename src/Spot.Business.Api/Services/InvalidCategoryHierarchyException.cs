namespace Spot.Business.Api.Services;

/// <summary>
/// Raised when a create/update request would break the single-level category hierarchy: a
/// category made its own parent, a subcategory used as someone else's parent, or a category that
/// already has subcategories turned into a subcategory itself.
/// </summary>
public sealed class InvalidCategoryHierarchyException(string message) : Exception(message);
