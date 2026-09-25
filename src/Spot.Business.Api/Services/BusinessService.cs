using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Services;

public sealed class BusinessService(IBusinessRepository repository) : IBusinessService
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

    /// <summary>
    /// The single ownership check behind every /{businessId} write: null if the business doesn't
    /// exist (404), <see cref="BusinessAccessDeniedException"/> if it isn't the caller's (403).
    /// </summary>
    private async Task<BusinessEntity?> GetOwnedAsync(Guid businessId, Guid callerId, CancellationToken ct)
    {
        var business = await repository.GetByIdAsync(businessId, ct);
        if (business is null)
            return null;

        if (business.AccountId != callerId)
            throw new BusinessAccessDeniedException(businessId, callerId);

        return business;
    }

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
