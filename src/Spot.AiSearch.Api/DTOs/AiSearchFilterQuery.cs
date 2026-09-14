using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.DTOs;

public class AiSearchFilterQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? UserId { get; set; }
    public AiRequestStatus? Status { get; set; }
    public AiRequestType? RequestType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
