namespace Spot.Business.Api.Services;

/// <summary>Raised when an operation on a resource nested under a business (e.g. its contacts) targets a business that doesn't exist.</summary>
public sealed class BusinessNotFoundException(Guid businessId)
    : Exception($"Business '{businessId}' does not exist.")
{
    public Guid BusinessId { get; } = businessId;
}
