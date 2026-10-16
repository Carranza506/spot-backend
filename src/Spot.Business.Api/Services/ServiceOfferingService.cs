using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Services;

public sealed class ServiceOfferingService(
    IServiceOfferingRepository repository, IBusinessRepository businessRepository) : IServiceOfferingService
{
    public async Task<ServiceDto?> CreateAsync(
        Guid businessId, Guid callerId, ServiceCreateRequest request, CancellationToken ct = default)
    {
        // Reuses the shared BusinessOwnership check (#122) on the repository: null if the business
        // doesn't exist (404), throws BusinessAccessDeniedException if it isn't the caller's (403).
        // An inactive business still counts — its owner can keep managing it, same as PATCH /{businessId}.
        if (await businessRepository.GetOwnedAsync(businessId, callerId, ct) is null)
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

    public async Task<ServiceDto?> UpdateAsync(
        Guid serviceId, Guid callerId, ServiceUpdateRequest request, CancellationToken ct = default)
    {
        var service = await repository.GetTrackedByIdAsync(serviceId, ct);
        if (service is null)
            return null;

        // Ownership is resolved through the service's business (the route has no businessId). Reuses
        // the shared BusinessOwnership check (#122): throws BusinessAccessDeniedException (403) if the
        // caller isn't the owner. The owner can edit even an inactive business, same as CreateAsync.
        _ = await businessRepository.GetOwnedAsync(service.BusinessId, callerId, ct);

        // Partial update: only the fields actually sent are applied.
        if (request.Name is not null)
            service.Name = request.Name.Trim();

        if (request.Description.IsSet)
            service.Description = BusinessFieldRules.Normalize(request.Description.Value);

        if (request.Price is { } price)
            // services.price is NUMERIC(12,2): round so the response matches what Postgres stores.
            service.Price = Math.Round(price, 2, MidpointRounding.AwayFromZero);

        if (request.DurationMinutes is { } duration)
            service.DurationMinutes = duration;

        if (request.IsActive is { } isActive)
            service.IsActive = isActive;

        await repository.SaveChangesAsync(service, ct);
        return ServiceDto.FromEntity(service);
    }

    public async Task<bool> DeactivateAsync(Guid serviceId, Guid callerId, CancellationToken ct = default)
    {
        var service = await repository.GetTrackedByIdAsync(serviceId, ct);
        if (service is null)
            return false;

        // Ownership via the service's business, reusing the shared GetOwnedAsync (403 if not owner).
        _ = await businessRepository.GetOwnedAsync(service.BusinessId, callerId, ct);

        // Soft delete: flip the flag, never remove the row (bookings reference service_id).
        // Idempotent — if it's already inactive there's nothing to persist.
        if (service.IsActive)
        {
            service.IsActive = false;
            await repository.SaveChangesAsync(service, ct);
        }

        return true;
    }
}
