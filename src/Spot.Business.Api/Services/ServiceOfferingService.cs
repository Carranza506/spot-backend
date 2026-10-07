using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Services;

public sealed class ServiceOfferingService(
    IServiceOfferingRepository repository, IBusinessService businessService) : IServiceOfferingService
{
    public async Task<ServiceDto?> CreateAsync(
        Guid businessId, Guid callerId, ServiceCreateRequest request, CancellationToken ct = default)
    {
        // An inactive business still counts — its owner can keep managing it, same as PATCH /{businessId}.
        if (await businessService.GetOwnedAsync(businessId, callerId, ct) is null)
            return null;

        var service = new ServiceEntity
        {
            BusinessId = businessId,
            Name = request.Name.Trim(),
            Description = BusinessFieldRules.Normalize(request.Description),
            // services.price is NUMERIC(12,2): round here so the 201 response matches what Postgres stores,
            // instead of returning more decimals than the column keeps.
            Price = Math.Round(request.Price!.Value, 2, MidpointRounding.AwayFromZero),
            DurationMinutes = request.DurationMinutes!.Value,
            IsActive = true,
        };

        await repository.CreateAsync(service, ct);
        return ServiceDto.FromEntity(service);
    }
}
