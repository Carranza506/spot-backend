using System.Text.Json.Serialization;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "Photo" schema in contracts/spot-api.yaml, for business and service photos alike.
/// Never exposes the storage key or the owning business/service ids beyond what the URL implies.
/// </summary>
/// <param name="Url">Absolute URL of GET /business/media/photos/{fileName}.</param>
/// <param name="IsPrimary">Business photos only; omitted from the JSON for service photos (contract).</param>
public sealed record PhotoDto(
    Guid Id,
    string Url,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsPrimary,
    int DisplayOrder,
    DateTimeOffset CreatedAt);
