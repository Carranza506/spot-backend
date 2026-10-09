using Spot.Business.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Services;

public interface IBusinessService
{
    /// <summary>Creates the business of <paramref name="accountId"/> (the caller's JWT sub), always active, with a slug generated from its name.</summary>
    /// <exception cref="Repositories.BusinessAlreadyExistsException">The account already has a business (active or not).</exception>
    Task<BusinessDto> CreateAsync(Guid accountId, BusinessCreateRequest request, CancellationToken ct = default);

    /// <summary>Public detail view: null if no business with <paramref name="businessId"/> exists or it's inactive.</summary>
    Task<BusinessDto?> GetPublicAsync(Guid businessId, CancellationToken ct = default);

    /// <summary>
    /// Public search of active businesses, paged. Every filter in <paramref name="query"/> is
    /// optional and combines with AND. Callers validate page/pageSize before calling.
    /// </summary>
    Task<PaginatedResponse<BusinessDto>> SearchAsync(BusinessSearchQuery query, CancellationToken ct = default);

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

    /// <summary>Public: the categories assigned to a business, paged. Null if no business with <paramref name="businessId"/> exists.</summary>
    Task<PaginatedResponse<CategoryDto>?> ListCategoriesAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Replaces the full category set of a business. Null if no business with <paramref name="businessId"/> exists.
    /// </summary>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    /// <exception cref="CategoryNotFoundException">One of <paramref name="categoryIds"/> doesn't exist.</exception>
    Task<IReadOnlyList<CategoryDto>?> ReplaceCategoriesAsync(
        Guid businessId, Guid callerId, IReadOnlyCollection<Guid> categoryIds, CancellationToken ct = default);

    /// <summary>
    /// The favorite businesses of <paramref name="userId"/> (the caller's JWT sub), newest first and
    /// paged. Favorites of an inactive business are left out and not counted.
    /// </summary>
    Task<PaginatedResponse<FavoriteBusinessDto>> ListFavoritesAsync(
        Guid userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Idempotently saves a business as a favorite of <paramref name="userId"/>; an existing
    /// favorite keeps its original created_at.
    /// </summary>
    /// <returns>False if no business with <paramref name="businessId"/> exists or it's inactive — even if it's already a favorite.</returns>
    Task<bool> AddFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default);

    /// <summary>
    /// Idempotently removes a business from the favorites of <paramref name="userId"/>: succeeds
    /// whether or not it was a favorite and whether or not the business exists or is active.
    /// </summary>
    Task RemoveFavoriteAsync(Guid userId, Guid businessId, CancellationToken ct = default);

    /// <summary>
    /// Public: the weekly schedule of a business, ordered by day of week (0 = Sunday) and paged.
    /// Null if no business with <paramref name="businessId"/> exists or it's inactive (same visibility as <see cref="GetPublicAsync"/>).
    /// </summary>
    Task<PaginatedResponse<BusinessHourDto>?> ListHoursAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Replaces the full weekly schedule of a business: days not in <paramref name="hours"/> are
    /// deleted. Works on an inactive business too. Null if no business with <paramref name="businessId"/> exists.
    /// </summary>
    /// <exception cref="BusinessAccessDeniedException"><paramref name="callerId"/> doesn't own the business.</exception>
    /// <exception cref="InvalidBusinessHoursException">A schedule rule is broken (duplicate day, open day without times, openTime >= closeTime).</exception>
    Task<IReadOnlyList<BusinessHourDto>?> ReplaceHoursAsync(
        Guid businessId, Guid callerId, IReadOnlyList<BusinessHourInput> hours, CancellationToken ct = default);
}
