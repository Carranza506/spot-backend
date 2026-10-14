namespace Spot.Business.Api.Services;

/// <summary>
/// Raised when the business exists (and is visible) but hasn't registered a location yet — no
/// business_locations row. Lets the controller answer 404 with a different message than for a
/// business that doesn't exist.
/// </summary>
public sealed class BusinessLocationNotSetException(Guid businessId)
    : Exception($"Business '{businessId}' has no location yet.")
{
    public Guid BusinessId { get; } = businessId;
}
