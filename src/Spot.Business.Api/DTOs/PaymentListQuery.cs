namespace Spot.Business.Api.DTOs;

/// <summary>Query parameters for GET /business/billing/payments (contract: PageParam/PageSizeParam).</summary>
public sealed class PaymentListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Same guard every other listing applies (see PhotoListQuery).</summary>
    public bool IsValid => Page >= 1 && PageSize is >= 1 and <= 100;
}
