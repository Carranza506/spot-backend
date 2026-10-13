using Spot.Business.Api.DTOs;
using Spot.Business.Api.Models;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public sealed class BusinessScheduleExceptionService(
    IBusinessRepository businessRepository,
    IBusinessScheduleExceptionRepository exceptionRepository) : IBusinessScheduleExceptionService
{
    public async Task<PaginatedResponse<BusinessScheduleExceptionDto>?> ListAsync(
        Guid businessId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var business = await businessRepository.GetByIdAsync(businessId, ct);
        if (business is not { IsActive: true })
            return null;

        var (items, total) = await exceptionRepository.ListByBusinessAsync(businessId, from, to, page, pageSize, ct);

        var dtos = items.Select(BusinessScheduleExceptionDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new PaginatedResponse<BusinessScheduleExceptionDto>(dtos, new PaginationMeta(page, pageSize, total, totalPages));
    }

    public async Task<BusinessScheduleExceptionDto> CreateAsync(
        Guid businessId, Guid callerId, BusinessScheduleExceptionCreateRequest request, CancellationToken ct = default)
    {
        await RequireOwnedAsync(businessId, callerId, ct);

        var isClosed = request.IsClosed!.Value;
        var (openTime, closeTime) = OpeningHours.Parse(isClosed, request.OpenTime, request.CloseTime);

        var exception = new BusinessScheduleException
        {
            BusinessId = businessId,
            ExceptionDate = request.ExceptionDate!.Value,
            IsClosed = isClosed,
            OpenTime = openTime,
            CloseTime = closeTime,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        };

        await exceptionRepository.CreateAsync(exception, ct);

        return BusinessScheduleExceptionDto.FromEntity(exception);
    }

    public async Task<bool> DeleteAsync(Guid businessId, Guid exceptionId, Guid callerId, CancellationToken ct = default)
    {
        await RequireOwnedAsync(businessId, callerId, ct);

        var exception = await exceptionRepository.GetByIdAsync(businessId, exceptionId, ct);
        if (exception is null)
            return false;

        await exceptionRepository.DeleteAsync(exception, ct);
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
