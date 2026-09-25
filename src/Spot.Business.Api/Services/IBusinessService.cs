using Spot.Business.Api.DTOs;

namespace Spot.Business.Api.Services;

public interface IBusinessService
{
    /// <summary>Creates the business of <paramref name="accountId"/> (the caller's JWT sub), always active, with a slug generated from its name.</summary>
    /// <exception cref="Repositories.BusinessAlreadyExistsException">The account already has a business (active or not).</exception>
    Task<BusinessDto> CreateAsync(Guid accountId, BusinessCreateRequest request, CancellationToken ct = default);

    /// <summary>Public detail view: null if no business with <paramref name="businessId"/> exists or it's inactive.</summary>
    Task<BusinessDto?> GetPublicAsync(Guid businessId, CancellationToken ct = default);

    /// <summary>The business of <paramref name="accountId"/>, active or not; null if the account has none.</summary>
    Task<BusinessDto?> GetOwnAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Null if no business with <paramref name="businessId"/> exists (active or not).</summary>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    Task<BusinessDto?> UpdateAsync(Guid businessId, Guid callerId, BusinessUpdateRequest request, CancellationToken ct = default);

    /// <summary>Null if <paramref name="accountId"/> has no business.</summary>
    Task<BusinessDto?> UpdateOwnAsync(Guid accountId, BusinessUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Soft delete (is_active = false) — never a physical delete. Idempotent: deactivating an
    /// already inactive business still succeeds.
    /// </summary>
    /// <returns>False if no business with <paramref name="businessId"/> exists.</returns>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    Task<bool> DeactivateAsync(Guid businessId, Guid callerId, CancellationToken ct = default);
}
