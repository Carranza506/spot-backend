namespace Spot.Business.Api.DTOs;

/// <summary>Query parameters for <c>GET /business/businesses/{businessId}/contacts</c> (contract: PageParam/PageSizeParam).</summary>
public sealed class BusinessContactListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
