using NetTopologySuite.Geometries;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "GeoPoint" schema in contracts/spot-api.yaml, as returned in responses. Note the
/// axis swap: NetTopologySuite stores (X, Y) = (longitude, latitude).
/// </summary>
public sealed record GeoPointDto(double Latitude, double Longitude)
{
    public static GeoPointDto FromPoint(Point point) => new(Latitude: point.Y, Longitude: point.X);
}
