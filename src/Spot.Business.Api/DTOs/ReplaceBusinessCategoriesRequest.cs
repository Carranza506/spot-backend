using System.ComponentModel.DataAnnotations;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the request body of PUT /business/businesses/{businessId}/categories.</summary>
public sealed class ReplaceBusinessCategoriesRequest
{
    [Required]
    [MinLength(1)]
    public List<Guid> CategoryIds { get; set; } = [];
}
