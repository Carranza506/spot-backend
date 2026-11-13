namespace Spot.Business.Api.Repositories;

/// <summary>
/// Raised when a business's slug collides with an existing one on the database's unique index.
/// Never reaches the client: BusinessService retries with the next suffix.
/// </summary>
public sealed class DuplicateBusinessSlugException(string slug)
    : Exception($"A business with slug '{slug}' already exists.")
{
    public string Slug { get; } = slug;
}
