namespace Spot.Business.Api.DTOs;

/// <summary>
/// Raw query parameters for <c>GET /business/businesses</c> (searchBusinesses), bound as-is by
/// model binding. All filters are optional and combine with AND. The proximity params
/// (<c>lat</c>/<c>lng</c>/<c>radiusKm</c>) in the contract are deliberately NOT declared here:
/// they belong to #119 (PostGIS), so for now they're simply ignored, never a 400. Same shape as
/// <see cref="CategoryFilterQuery"/>.
/// </summary>
public sealed class BusinessSearchQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Free text matched (case-insensitively) against the business name and description.</summary>
    public string? Q { get; set; }

    public Guid? CategoryId { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
}
