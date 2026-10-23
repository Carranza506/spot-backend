namespace Spot.AiSearch.Api.DTOs;

/// <summary>
/// Matches the "Business" schema in contracts/spot-api.yaml, as returned by Spot.Business.Api's
/// GET /business/businesses. Passed through as-is: never built from anything the AI model said.
/// </summary>
public record BusinessDto(
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
    DateTimeOffset UpdatedAt
);
