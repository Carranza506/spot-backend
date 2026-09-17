using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public sealed class CategoryService(ICategoryRepository repository) : ICategoryService
{
    public async Task<PaginatedResponse<CategoryDto>> ListAsync(
        int page,
        int pageSize,
        ParentCategoryFilter parentFilter,
        Guid? parentCategoryId,
        CancellationToken ct = default)
    {
        var (items, total) = await repository.ListAsync(parentFilter, parentCategoryId, page, pageSize, ct);

        var dtos = items.Select(CategoryDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var pagination = new PaginationMeta(page, pageSize, total, totalPages);

        return new PaginatedResponse<CategoryDto>(dtos, pagination);
    }
}
