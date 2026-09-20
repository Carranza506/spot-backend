namespace Spot.Business.Api.Repositories;

/// <summary>Raised when a category with the same (ParentCategoryId, Name) pair already exists.</summary>
public sealed class DuplicateCategoryException(Guid? parentCategoryId, string name)
    : Exception($"A category named '{name}' already exists under parent '{parentCategoryId?.ToString() ?? "null"}'.")
{
    public Guid? ParentCategoryId { get; } = parentCategoryId;
    public string Name { get; } = name;
}
