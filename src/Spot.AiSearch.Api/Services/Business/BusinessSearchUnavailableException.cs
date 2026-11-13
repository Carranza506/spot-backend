namespace Spot.AiSearch.Api.Services.Business;

/// <summary>Spot.Business.Api couldn't run the search (down, timed out, error status or unreadable body).</summary>
public sealed class BusinessSearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
