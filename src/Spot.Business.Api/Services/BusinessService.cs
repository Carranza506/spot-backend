using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Shared.Pagination;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Services;

public sealed class BusinessService(IBusinessRepository repository, ICategoryRepository categoryRepository) : IBusinessService
{
    /// <summary>
    /// How many times a create is retried after losing a slug race on IX_businesses_slug (each
    /// retry moves to the next free suffix). Only concurrent creates of same-named businesses can
    /// get here, so a handful is plenty; past that the exception surfaces as a 500.
    /// </summary>
    private const int MaxSlugAttempts = 5;

    public async Task<BusinessDto> CreateAsync(Guid accountId, BusinessCreateRequest request, CancellationToken ct = default)
    {
        if (await repository.ExistsByAccountIdAsync(accountId, ct))
            throw new BusinessAlreadyExistsException(accountId);

        var name = request.Name.Trim();
        var business = new BusinessEntity
        {
            AccountId = accountId,
            Name = name,
            Description = BusinessFieldRules.Normalize(request.Description),
            LegalName = BusinessFieldRules.Normalize(request.LegalName),
            Email = BusinessFieldRules.Normalize(request.Email),
            Phone = BusinessFieldRules.Normalize(request.Phone),
            Website = BusinessFieldRules.Normalize(request.Website),
            LogoUrl = BusinessFieldRules.Normalize(request.LogoUrl),
            IsActive = true,
        };

        var baseSlug = SlugGenerator.FromName(name);
        var suffix = 1;

        for (var attempt = 1; ; attempt++)
        {
            while (await repository.SlugExistsAsync(SlugGenerator.WithSuffix(baseSlug, suffix), ct))
                suffix++;

            business.Slug = SlugGenerator.WithSuffix(baseSlug, suffix);

            try
            {
                await repository.CreateAsync(business, ct);
                return BusinessDto.FromEntity(business);
            }
            catch (DuplicateBusinessSlugException) when (attempt < MaxSlugAttempts)
            {
                suffix++;
            }
        }
    }

    public async Task<BusinessDto?> GetPublicAsync(Guid businessId, CancellationToken ct = default)
    {
        var business = await repository.GetByIdAsync(businessId, ct);
        return business is { IsActive: true } ? BusinessDto.FromEntity(business) : null;
    }

    public async Task<BusinessDto?> GetOwnAsync(Guid accountId, CancellationToken ct = default)
    {
        var business = await repository.GetByAccountIdAsync(accountId, ct);
        return business is null ? null : BusinessDto.FromEntity(business);
    }

    public async Task<BusinessDto?> UpdateAsync(
        Guid businessId, Guid callerId, BusinessUpdateRequest request, CancellationToken ct = default)
    {
        var business = await GetOwnedAsync(businessId, callerId, ct);
        if (business is null)
            return null;

        await ApplyUpdateAsync(business, request, ct);
        return BusinessDto.FromEntity(business);
    }

    public async Task<BusinessDto?> UpdateOwnAsync(Guid accountId, BusinessUpdateRequest request, CancellationToken ct = default)
    {
        var business = await repository.GetByAccountIdAsync(accountId, ct);
        if (business is null)
            return null;

        await ApplyUpdateAsync(business, request, ct);
        return BusinessDto.FromEntity(business);
    }

    public async Task<bool> DeactivateAsync(Guid businessId, Guid callerId, CancellationToken ct = default)
    {
        var business = await GetOwnedAsync(businessId, callerId, ct);
        if (business is null)
            return false;

        if (business.IsActive)
        {
            business.IsActive = false;
            await repository.SaveChangesAsync(business, ct);
        }

        return true;
    }

    public async Task<PaginatedResponse<CategoryDto>?> ListCategoriesAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var business = await repository.GetByIdAsync(businessId, ct);
        if (business is null)
            return null;

        var (items, total) = await repository.ListCategoriesAsync(businessId, page, pageSize, ct);
        var dtos = items.Select(CategoryDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new PaginatedResponse<CategoryDto>(dtos, new PaginationMeta(page, pageSize, total, totalPages));
    }

    public async Task<IReadOnlyList<CategoryDto>?> ReplaceCategoriesAsync(
        Guid businessId, Guid callerId, IReadOnlyCollection<Guid> categoryIds, CancellationToken ct = default)
    {
        var business = await GetOwnedAsync(businessId, callerId, ct);
        if (business is null)
            return null;

        var distinctIds = categoryIds.Distinct().ToList();
        var existingIds = new HashSet<Guid>(await categoryRepository.ExistingIdsAsync(distinctIds, ct));

        foreach (var categoryId in distinctIds)
        {
            if (!existingIds.Contains(categoryId))
                throw new CategoryNotFoundException(categoryId);
        }

        var categories = await repository.ReplaceCategoriesAsync(businessId, distinctIds, ct);
        return categories.Select(CategoryDto.FromEntity).ToList();
    }

    private Task<BusinessEntity?> GetOwnedAsync(Guid businessId, Guid callerId, CancellationToken ct) =>
        repository.GetOwnedAsync(businessId, callerId, ct);

    /// <summary>
    /// Shared by PATCH /{businessId} and PATCH /me. The slug is deliberately left untouched on a
    /// rename so existing URLs keep working.
    /// </summary>
    private async Task ApplyUpdateAsync(BusinessEntity business, BusinessUpdateRequest request, CancellationToken ct)
    {
        if (request.Name is not null)
            business.Name = request.Name.Trim();

        if (request.Description.IsSet)
            business.Description = BusinessFieldRules.Normalize(request.Description.Value);

        if (request.LegalName.IsSet)
            business.LegalName = BusinessFieldRules.Normalize(request.LegalName.Value);

        if (request.Email.IsSet)
            business.Email = BusinessFieldRules.Normalize(request.Email.Value);

        if (request.Phone.IsSet)
            business.Phone = BusinessFieldRules.Normalize(request.Phone.Value);

        if (request.Website.IsSet)
            business.Website = BusinessFieldRules.Normalize(request.Website.Value);

        if (request.LogoUrl.IsSet)
            business.LogoUrl = BusinessFieldRules.Normalize(request.LogoUrl.Value);

        if (request.IsActive.HasValue)
            business.IsActive = request.IsActive.Value;

        await repository.SaveChangesAsync(business, ct);
    }
}
