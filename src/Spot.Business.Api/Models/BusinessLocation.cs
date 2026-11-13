using NetTopologySuite.Geometries;

namespace Spot.Business.Api.Models;

public class BusinessLocation
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Address { get; set; } = null!;
    public string? City { get; set; }
    public string? Province { get; set; }
    public string Country { get; set; } = "Costa Rica";
    public string? PostalCode { get; set; }

    /// <summary>
    /// geography(Point,4326). NetTopologySuite uses (X, Y) = (longitude, latitude) — the reverse
    /// of the contract's GeoPoint field order — and the point must carry SRID 4326.
    /// </summary>
    public Point Location { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Business Business { get; set; } = null!;
}
