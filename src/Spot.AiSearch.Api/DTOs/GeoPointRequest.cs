using System.ComponentModel.DataAnnotations;

namespace Spot.AiSearch.Api.DTOs;

/// <summary>
/// Matches the "GeoPoint" schema in contracts/spot-api.yaml, as received in request bodies. Same
/// shape and ranges as Spot.Business.Api's GeoPointRequest. Nullable so a missing coordinate is a
/// 400 instead of silently binding to 0 (a real point in the Gulf of Guinea).
/// </summary>
public sealed class GeoPointRequest
{
    [Required]
    [Range(-90d, 90d)]
    public double? Latitude { get; set; }

    [Required]
    [Range(-180d, 180d)]
    public double? Longitude { get; set; }
}
