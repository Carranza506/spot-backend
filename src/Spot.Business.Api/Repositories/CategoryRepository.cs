using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public sealed class CategoryRepository(BusinessDbContext db) : ICategoryRepository
{
    public async Task<(IReadOnlyList<Category> Items, int Total)> ListAsync(
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = db.Categories.AsNoTracking();

        query = parentFilter switch
        {
            ParentCategoryFilter.RootOnly => query.Where(c => c.ParentCategoryId == null),
            ParentCategoryFilter.Specific => query.Where(c => c.ParentCategoryId == parentCategoryId),
            _ => query,
        };

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
