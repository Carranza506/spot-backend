namespace Spot.Business.Api.DTOs;

/// <summary>Query parameters for the photo listings (contract: PageParam/PageSizeParam).</summary>
public sealed class PhotoListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Same guard every other listing applies (see BusinessContactsController).</summary>
    public bool IsValid => Page >= 1 && PageSize is >= 1 and <= 100;
}
