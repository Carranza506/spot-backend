using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "Service" schema in contracts/spot-api.yaml.</summary>
public sealed record ServiceDto(
    Guid Id,
    Guid BusinessId,
    string Name,
    string? Description,
    decimal Price,
    int DurationMinutes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ServiceDto FromEntity(ServiceEntity service) => new(
        Id: service.Id,
        BusinessId: service.BusinessId,
        Name: service.Name,
        Description: service.Description,
        Price: service.Price,
        DurationMinutes: service.DurationMinutes,
        IsActive: service.IsActive,
        CreatedAt: service.CreatedAt,
        UpdatedAt: service.UpdatedAt);
}
