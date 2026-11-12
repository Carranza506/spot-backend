namespace Spot.TestSupport;

/// <summary>
/// A clock frozen at <see cref="Now"/>, so time-based rules ("slots in the past are excluded",
/// "has the period ended?") are deterministic. Tests may move it by setting <see cref="Now"/>.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
