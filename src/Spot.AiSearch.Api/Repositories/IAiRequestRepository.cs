using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.Repositories;

public interface IAiRequestRepository
{
    Task<(IReadOnlyList<AiRequest> Items, int Total)> ListAsync(AiSearchFilterQuery filter, CancellationToken ct = default);

    /// <summary>Inserts <paramref name="entity"/> as a new ai_requests row and saves it.</summary>
    Task AddAsync(AiRequest entity, CancellationToken ct = default);
}
