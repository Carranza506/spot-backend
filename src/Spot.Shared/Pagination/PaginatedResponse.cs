namespace Spot.Shared.Pagination;

public record PaginatedResponse<T>(IReadOnlyList<T> Data, PaginationMeta Pagination);
