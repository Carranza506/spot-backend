namespace Spot.Shared.Errors;

/// <summary>
/// Shape of every error response in the API, matching the <c>Error</c> schema in
/// contracts/spot-api.yaml (<c>code</c>, <c>message</c>, <c>details</c>, <c>timestamp</c>).
/// <c>Code</c> is a stable, machine-readable identifier (e.g. "EMAIL_ALREADY_REGISTERED"),
/// while <c>Message</c> is meant to be shown to the end user as-is.
/// </summary>
public record ApiError(string Code, string Message, object? Details = null)
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}
