namespace Spot.Shared.Errors;

public record ApiError(string Code, string Message, object? Details = null)
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}
