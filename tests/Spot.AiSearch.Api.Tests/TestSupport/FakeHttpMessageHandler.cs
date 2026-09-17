namespace Spot.AiSearch.Api.Tests.TestSupport;

/// <summary>
/// Stands in for the network so <see cref="Services.Gemini.GeminiClient"/> can be tested against
/// canned Gemini API responses (and simulated failures/timeouts) without a real HttpClient
/// handler talking to the internet.
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
