using Spot.Business.Api.Repositories;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Services;

/// <summary>
/// The single ownership check behind every write on a specific business — the business itself
/// (#56) and anything nested under it, e.g. its contacts (#59). Ownership means
/// businesses.account_id equals the caller's JWT sub.
/// </summary>
public static class BusinessOwnership
{
    /// <summary>
    /// Null if the business doesn't exist, active or not (404);
    /// <see cref="BusinessAccessDeniedException"/> if it isn't the caller's (403). The returned
    /// entity is tracked, same as <see cref="IBusinessRepository.GetByIdAsync"/>.
    /// </summary>
    public static async Task<BusinessEntity?> GetOwnedAsync(
        this IBusinessRepository repository, Guid businessId, Guid callerId, CancellationToken ct = default)
    {
        var business = await repository.GetByIdAsync(businessId, ct);
        if (business is null)
            return null;

        if (business.AccountId != callerId)
            throw new BusinessAccessDeniedException(businessId, callerId);

        return business;
    }
}
