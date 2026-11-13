namespace Spot.Business.Api.Services;

/// <summary>Raised when a create/update request references a parent category id that doesn't exist.</summary>
public sealed class CategoryNotFoundException(Guid categoryId)
    : Exception($"No category with id '{categoryId}' exists.")
{
    public Guid CategoryId { get; } = categoryId;
}
