namespace Spot.Business.Api.Repositories;

/// <summary>Raised when an account that already has a business (businesses.account_id is unique) tries to create another.</summary>
public sealed class BusinessAlreadyExistsException(Guid accountId)
    : Exception($"Account '{accountId}' already has a business.")
{
    public Guid AccountId { get; } = accountId;
}
