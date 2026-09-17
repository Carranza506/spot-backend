using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public interface ICategoryRepository
{
    /// <summary>
    /// Lists categories, ordered by name, filtered per <paramref name="parentFilter"/> and paged.
    /// </summary>
    Task<(IReadOnlyList<Category> Items, int Total)> ListAsync(
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
