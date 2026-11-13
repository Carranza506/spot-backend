namespace Spot.TestSupport;

/// <summary>
/// Stands in for the network, so an HTTP client (Gemini, FCM, ...) can be tested against canned
/// responses and simulated failures or timeouts without leaving the process.
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
