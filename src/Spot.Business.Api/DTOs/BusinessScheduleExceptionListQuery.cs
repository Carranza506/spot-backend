namespace Spot.Business.Api.DTOs;

/// <summary>
/// Query parameters for <c>GET /business/businesses/{businessId}/schedule-exceptions</c> (contract:
/// PageParam/PageSizeParam, from, to). <see cref="From"/>/<see cref="To"/> are bound as strings on
/// purpose: binding them as DateOnly would go through culture-dependent parsing that also accepts
/// formats other than YYYY-MM-DD. The controller parses them strictly.
/// </summary>
public sealed class BusinessScheduleExceptionListQuery
{
    public const string DateFormat = "yyyy-MM-dd";

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? From { get; set; }
    public string? To { get; set; }
}
