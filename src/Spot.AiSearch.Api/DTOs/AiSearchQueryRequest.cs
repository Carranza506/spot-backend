using System.ComponentModel.DataAnnotations;

namespace Spot.AiSearch.Api.DTOs;

/// <summary>
/// Matches the "AiSearchQueryRequest" schema in contracts/spot-api.yaml. Every limit comes from the
/// contract and fails model binding with a 400 in the Error shape (see Program.cs). RadiusKm, Page
/// and PageSize aren't nullable, so an explicit null is a 400 too instead of falling back to the default.
/// </summary>
public sealed class AiSearchQueryRequest
{
    public const int QueryMinLength = 3;
    public const int QueryMaxLength = 500;

    [Required]
    [StringLength(QueryMaxLength, MinimumLength = QueryMinLength)]
    public string? Query { get; set; }

    public GeoPointRequest? Location { get; set; }

    /// <summary>Only used together with <see cref="Location"/>.</summary>
    [Range(0.1d, 100d)]
    public double RadiusKm { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// <see cref="Query"/> as it's validated, sent to the AI model and stored in ai_requests.prompt:
    /// NUL becomes a space (Postgres text can't hold it) and the ends are trimmed. Call only after
    /// model validation, when <see cref="Query"/> is set.
    /// </summary>
    public string NormalizedQuery() => Query!.Replace('\0', ' ').Trim();
}
