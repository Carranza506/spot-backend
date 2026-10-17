using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;
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

    public async Task<PaginatedResponse<ServiceDto>?> ListAsync(
        Guid businessId, int page, int pageSize, bool? isActive, Guid? callerId, CancellationToken ct = default)
    {
        var business = await businessRepository.GetByIdAsync(businessId, ct);
        if (business is null)
            return null;

        // Soft ownership check: just "is the caller the owner?", NOT the throwing GetOwnedAsync used
        // by writes — a non-owner here isn't forbidden, only restricted to the public view.
        var isOwner = callerId is { } caller && business.AccountId == caller;

        // Public/non-owner can't see an inactive business at all.
        if (!isOwner && !business.IsActive)
            return null;

        // Owner: isActive filters freely (null = all). Everyone else: only active, always.
        bool? activeFilter = isOwner ? isActive : true;

        var (items, total) = await repository.ListAsync(businessId, page, pageSize, activeFilter, ct);
        var dtos = items.Select(ServiceDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new PaginatedResponse<ServiceDto>(dtos, new PaginationMeta(page, pageSize, total, totalPages));
    }

    public async Task<ServiceDto?> GetAsync(Guid serviceId, Guid? callerId, CancellationToken ct = default)
    {
        var service = await repository.GetByIdAsync(serviceId, ct);
        if (service is null)
            return null;

        var business = await businessRepository.GetByIdAsync(service.BusinessId, ct);
        if (business is null)
            return null;

        // Same soft ownership check as the list. The owner sees their own service regardless of its
        // (or the business's) active state; everyone else only sees an active service of an active business.
        var isOwner = callerId is { } caller && business.AccountId == caller;
        if (!isOwner && (!service.IsActive || !business.IsActive))
            return null;

        return ServiceDto.FromEntity(service);
    }
}
