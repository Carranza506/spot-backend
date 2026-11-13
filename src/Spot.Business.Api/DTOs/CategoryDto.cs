using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "Category" schema in contracts/spot-api.yaml.</summary>
public sealed record CategoryDto(
    Guid Id,
    Guid? ParentCategoryId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CategoryDto FromEntity(Category category) => new(
        Id: category.Id,
        ParentCategoryId: category.ParentCategoryId,
        Name: category.Name,
        Description: category.Description,
        IsActive: category.IsActive,
        CreatedAt: category.CreatedAt,
        UpdatedAt: category.UpdatedAt);
}
