using Spot.Notifications.Api.Services.Push;

namespace Spot.Notifications.Api.Tests.Fakes;

/// <summary>Records every push instead of sending it, and answers with a configurable summary.</summary>
public sealed class FakePushNotificationService : IPushNotificationService
{
    public sealed record SentPush(
        Guid UserId, string Title, string Body, IReadOnlyDictionary<string, string>? Data, CancellationToken Token);

    private readonly List<SentPush> _sent = [];

    public IReadOnlyList<SentPush> Sent => _sent;

    public PushDeliverySummary Summary { get; set; } = new(true, 1, 0, 0);

    /// <summary>Users whose push throws instead of being recorded.</summary>
    public HashSet<Guid> FailFor { get; } = [];

    public void Reset()
    {
        _sent.Clear();
        Summary = new PushDeliverySummary(true, 1, 0, 0);
        FailFor.Clear();
    }

    public Task<PushDeliverySummary> SendToUserAsync(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        if (FailFor.Contains(userId))
            throw new InvalidOperationException("Simulated push failure.");

        _sent.Add(new SentPush(userId, title, body, data, ct));
        return Task.FromResult(Summary);
    }
}
