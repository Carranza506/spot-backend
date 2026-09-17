using Spot.Business.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public interface ICategoryService
{
    Task<PaginatedResponse<CategoryDto>> ListAsync(
        int page,
        int pageSize,
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        CancellationToken ct = default);
}
