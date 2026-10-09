using System.Text.Json.Serialization;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "BusinessContact" schema in contracts/spot-api.yaml.</summary>
public sealed record BusinessContactDto(
    Guid Id,
    Guid BusinessId,
    [property: JsonConverter(typeof(ContactTypeJsonConverter))] ContactType Type,
    string Value,
    bool IsPrimary,
    DateTimeOffset CreatedAt)
{
    public static BusinessContactDto FromEntity(BusinessContact contact) => new(
        Id: contact.Id,
        BusinessId: contact.BusinessId,
        Type: contact.Type,
        Value: contact.Value,
        IsPrimary: contact.IsPrimary,
        CreatedAt: contact.CreatedAt);
}
