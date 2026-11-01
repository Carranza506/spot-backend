namespace Spot.AiSearch.Api.DTOs;

/// <summary>
/// Matches the "AiSearchCriteria" schema in contracts/spot-api.yaml: what the AI model extracted,
/// after validation (see AiSearchCriteriaParser). Every field is optional; a null one is left out.
/// </summary>
public record AiSearchCriteriaDto(
    string? ServiceQuery,
    Guid? CategoryId,
    DateOnly? Date,
    TimeOnly? Time,
    decimal? BudgetMaxColones,
    string? RawLocationText
);
