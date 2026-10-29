namespace Spot.Notifications.Api.Tests.TestSupport;

/// <summary>
/// Stands in for the network so <see cref="Services.Push.FcmPushSender"/> can be tested against
/// canned FCM responses (and simulated failures/timeouts) without talking to Google. Same as
/// Spot.AiSearch.Api.Tests' FakeHttpMessageHandler.
/// </summary>
public sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await handler(request, cancellationToken);
    }
}
