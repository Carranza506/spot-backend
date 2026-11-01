namespace Spot.AiSearch.Api.Services;

/// <summary>Nothing usable came out of the query: no service, no category and no location to search with.</summary>
public sealed class AiSearchNotUnderstoodException() : Exception("The query yielded no usable search criteria.");
