using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public interface IBusinessScheduleExceptionService
{
    /// <summary>
    /// Public listing ordered by date ascending, with optional inclusive <paramref name="from"/>/<paramref name="to"/> bounds.
    /// Null if the business doesn't exist or is inactive (same visibility as GET /business/businesses/{businessId}).
    /// </summary>
    Task<PaginatedResponse<BusinessScheduleExceptionDto>?> ListAsync(
        Guid businessId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Works on an inactive business too, same as PUT .../hours. A closed day stores null times.</summary>
    /// <exception cref="BusinessNotFoundException">The business doesn't exist.</exception>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    /// <exception cref="InvalidBusinessHoursException">MISSING_OPENING_HOURS or INVALID_TIME_RANGE.</exception>
    /// <exception cref="ScheduleExceptionAlreadyExistsException">The business already has an exception on that date.</exception>
    Task<BusinessScheduleExceptionDto> CreateAsync(
        Guid businessId, Guid callerId, BusinessScheduleExceptionCreateRequest request, CancellationToken ct = default);

    /// <summary>Hard delete. Works on an inactive business too.</summary>
    /// <returns>False if the exception doesn't exist or belongs to a different business.</returns>
    /// <exception cref="BusinessNotFoundException">The business doesn't exist.</exception>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    Task<bool> DeleteAsync(Guid businessId, Guid exceptionId, Guid callerId, CancellationToken ct = default);
}
