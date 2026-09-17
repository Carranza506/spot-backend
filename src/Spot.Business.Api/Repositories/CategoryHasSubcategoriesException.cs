namespace Spot.Business.Api.Repositories;

/// <summary>Raised when trying to delete a category that still has one or more subcategories.</summary>
public sealed class CategoryHasSubcategoriesException(Guid categoryId)
    : Exception($"Category '{categoryId}' has subcategories and cannot be deleted.")
{
    public Guid CategoryId { get; } = categoryId;
}
