using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.Repositories;

public interface IAiRequestRepository
{
    Task<(IReadOnlyList<AiRequest> Items, int Total)> ListAsync(AiSearchFilterQuery filter, CancellationToken ct = default);
}
