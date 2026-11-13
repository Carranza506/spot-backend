namespace Spot.Business.Api.DTOs;

/// <summary>
/// The un-paginated `{ data: [BusinessHour] }` response of PUT /business/businesses/{businessId}/hours
/// (contracts/spot-api.yaml) — unlike the GET on the same route, this one isn't PaginatedBusinessHours.
/// </summary>
public sealed record BusinessHourListResponse(IReadOnlyList<BusinessHourDto> Data);
