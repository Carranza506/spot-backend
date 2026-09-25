namespace Spot.Business.Api.Services;

/// <summary>Raised when the caller is not the BUSINESS account that owns the business (businesses.account_id != JWT sub).</summary>
public sealed class BusinessAccessDeniedException(Guid businessId, Guid callerId)
    : Exception($"Account '{callerId}' does not own business '{businessId}'.")
{
    public Guid BusinessId { get; } = businessId;
    public Guid CallerId { get; } = callerId;
}
