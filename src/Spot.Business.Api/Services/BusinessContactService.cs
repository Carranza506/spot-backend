using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public sealed class BusinessContactService(
    IBusinessRepository businessRepository,
    IBusinessContactRepository contactRepository) : IBusinessContactService
{
    public async Task<PaginatedResponse<BusinessContactDto>?> ListAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var business = await businessRepository.GetByIdAsync(businessId, ct);
        if (business is not { IsActive: true })
            return null;

        var (items, total) = await contactRepository.ListByBusinessAsync(businessId, page, pageSize, ct);

        var dtos = items.Select(BusinessContactDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new PaginatedResponse<BusinessContactDto>(dtos, new PaginationMeta(page, pageSize, total, totalPages));
    }

    public async Task<BusinessContactDto> CreateAsync(
        Guid businessId, Guid callerId, BusinessContactCreateRequest request, CancellationToken ct = default)
    {
        await RequireOwnedAsync(businessId, callerId, ct);

        var contact = new BusinessContact
        {
            BusinessId = businessId,
            Type = request.Type!.Value,
            Value = request.Value.Trim(),
            IsPrimary = request.IsPrimary,
        };

        await contactRepository.CreateAsync(contact, ct);

        return BusinessContactDto.FromEntity(contact);
    }

    public async Task<bool> DeleteAsync(Guid businessId, Guid contactId, Guid callerId, CancellationToken ct = default)
    {
        await RequireOwnedAsync(businessId, callerId, ct);

        var contact = await contactRepository.GetByIdAsync(businessId, contactId, ct);
        if (contact is null)
            return false;

        await contactRepository.DeleteAsync(contact, ct);
        return true;
    }

    /// <summary>
    /// #56's ownership check (<see cref="BusinessOwnership"/>): 404 if the business is missing,
    /// 403 if it isn't the caller's. An inactive business still counts — its owner can manage it.
    /// </summary>
    private async Task RequireOwnedAsync(Guid businessId, Guid callerId, CancellationToken ct)
    {
        if (await businessRepository.GetOwnedAsync(businessId, callerId, ct) is null)
            throw new BusinessNotFoundException(businessId);
    }
}
