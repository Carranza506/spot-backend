namespace Spot.Business.Api.Tests.Fakes;

/// <summary>A clock frozen at <see cref="Now"/>, so "has the period ended?" checks are deterministic.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
