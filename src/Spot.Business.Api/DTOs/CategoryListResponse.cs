namespace Spot.Business.Api.DTOs;

/// <summary>
/// The un-paginated `{ data: [Category] }` response of PUT /business/businesses/{businessId}/categories
/// (contracts/spot-api.yaml) — unlike the GET on the same route, this one isn't PaginatedCategories.
/// </summary>
public sealed record CategoryListResponse(IReadOnlyList<CategoryDto> Data);
