using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "Business" schema in contracts/spot-api.yaml. Deliberately has no AccountId —
/// the contract never exposes which account a business belongs to.
/// </summary>
public sealed record BusinessDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LegalName,
    string? Email,
    string? Phone,
    string? Website,
    string? LogoUrl,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BusinessDto FromEntity(BusinessEntity business) => new(
        Id: business.Id,
        Name: business.Name,
        Slug: business.Slug,
        Description: business.Description,
        LegalName: business.LegalName,
        Email: business.Email,
        Phone: business.Phone,
        Website: business.Website,
        LogoUrl: business.LogoUrl,
        IsActive: business.IsActive,
        CreatedAt: business.CreatedAt,
        UpdatedAt: business.UpdatedAt);
}
