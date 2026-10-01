using System.ComponentModel.DataAnnotations;
using NetTopologySuite.Geometries;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "GeoPoint" schema in contracts/spot-api.yaml, as received in request bodies.
/// Nullable so a missing coordinate is a 400 instead of silently binding to 0 (a real point in
/// the Gulf of Guinea).
/// </summary>
public sealed class GeoPointRequest
{
    public const int Srid = 4326;

    [Required]
    [Range(-90d, 90d)]
    public double? Latitude { get; set; }

    [Required]
    [Range(-180d, 180d)]
    public double? Longitude { get; set; }

    /// <summary>
    /// The geography(Point,4326) value stored in business_locations.location — X is longitude,
    /// Y is latitude. Only call after model validation has passed.
    /// </summary>
    public Point ToPoint() => new(Longitude!.Value, Latitude!.Value) { SRID = Srid };
}
