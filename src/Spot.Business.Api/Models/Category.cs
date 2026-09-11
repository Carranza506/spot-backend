namespace Spot.Business.Api.Models;

public class Category
{
    public Guid Id { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = [];
}
