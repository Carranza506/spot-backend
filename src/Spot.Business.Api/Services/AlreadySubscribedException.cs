namespace Spot.Business.Api.Services;

/// <summary>The account already has an ACTIVE/PAST_DUE subscription. Mapped to 409 ALREADY_SUBSCRIBED by the controller.</summary>
public sealed class AlreadySubscribedException(Guid accountId)
    : Exception($"Account '{accountId}' already has an active subscription.")
{
    public Guid AccountId { get; } = accountId;
}
