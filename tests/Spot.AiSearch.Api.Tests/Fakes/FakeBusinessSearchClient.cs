using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Services.Business;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Tests.Fakes;

/// <summary>Stands in for Spot.Business.Api in pipeline tests: returns <see cref="Businesses"/> as one page.</summary>
public sealed class FakeBusinessSearchClient : IBusinessSearchClient
{
    public List<BusinessDto> Businesses { get; } = [];

    /// <summary>When set, SearchAsync throws it instead of answering, like Business being down.</summary>
    public Exception? FailWith { get; set; }

    public BusinessSearchRequest? LastRequest { get; private set; }

    public Task<PaginatedResponse<BusinessDto>> SearchAsync(BusinessSearchRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        if (FailWith is not null)
            throw FailWith;

        var totalPages = (int)Math.Ceiling(Businesses.Count / (double)request.PageSize);
        return Task.FromResult(new PaginatedResponse<BusinessDto>(
            Businesses.ToList(), new PaginationMeta(request.Page, request.PageSize, Businesses.Count, totalPages)));
    }

    public void Reset()
    {
        Businesses.Clear();
        FailWith = null;
        LastRequest = null;
    }
}
