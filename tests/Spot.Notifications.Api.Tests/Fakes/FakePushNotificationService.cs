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

    public void Reset()
    {
        _sent.Clear();
        Summary = new PushDeliverySummary(true, 1, 0, 0);
    }

    public Task<PushDeliverySummary> SendToUserAsync(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        _sent.Add(new SentPush(userId, title, body, data, ct));
        return Task.FromResult(Summary);
    }
}
