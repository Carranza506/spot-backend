using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "BusinessLocation" schema in contracts/spot-api.yaml.</summary>
public sealed record BusinessLocationDto(
    Guid Id,
    Guid BusinessId,
    string Address,
    string? City,
    string? Province,
    string Country,
    string? PostalCode,
    GeoPointDto Location,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BusinessLocationDto FromEntity(BusinessLocation location) => new(
        Id: location.Id,
        BusinessId: location.BusinessId,
        Address: location.Address,
        City: location.City,
        Province: location.Province,
        Country: location.Country,
        PostalCode: location.PostalCode,
        Location: GeoPointDto.FromPoint(location.Location),
        CreatedAt: location.CreatedAt,
        UpdatedAt: location.UpdatedAt);
}
