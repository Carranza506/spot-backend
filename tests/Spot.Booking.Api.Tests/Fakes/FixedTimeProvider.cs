namespace Spot.Booking.Api.Tests.Fakes;

/// <summary>A clock frozen at <paramref name="now"/>, so "slots in the past are excluded" is deterministic.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
