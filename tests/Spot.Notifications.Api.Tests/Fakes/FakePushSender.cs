using Spot.Notifications.Api.Services.Push;

namespace Spot.Notifications.Api.Tests.Fakes;

/// <summary>Answers each push with <see cref="Respond"/> (by device token) and records what was sent.</summary>
public sealed class FakePushSender : IPushSender
{
    public Func<string, PushSendResult> Respond { get; set; } = _ => PushSendResult.Sent;

    public List<PushMessage> Sent { get; } = [];

    public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        return Task.FromResult(Respond(message.DeviceToken));
    }
}
