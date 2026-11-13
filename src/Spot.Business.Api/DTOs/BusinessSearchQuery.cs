namespace Spot.Business.Api.DTOs;

/// <summary>
/// Raw query parameters for <c>GET /business/businesses</c> (searchBusinesses), bound as-is by
/// model binding. All filters are optional and combine with AND. The proximity params
/// (<c>lat</c>/<c>lng</c>/<c>radiusKm</c>, #119) are an all-or-nothing trio validated by the
/// controller (a partial trio, or a <c>radiusKm</c> outside the contract range, is a 400 — never
/// silently ignored). Same shape as <see cref="CategoryFilterQuery"/>.
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

    /// <summary>Latitude of the proximity reference point. Only meaningful together with <see cref="Lng"/> and <see cref="RadiusKm"/>.</summary>
    public double? Lat { get; set; }

    /// <summary>Longitude of the proximity reference point. Only meaningful together with <see cref="Lat"/> and <see cref="RadiusKm"/>.</summary>
    public double? Lng { get; set; }

    /// <summary>Search radius in kilometers (contract range 0.1–100). Requires <see cref="Lat"/> and <see cref="Lng"/>.</summary>
    public double? RadiusKm { get; set; }
}
