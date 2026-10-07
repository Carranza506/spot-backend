using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Options;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.AiSearch.Api.Tests.TestSupport;

namespace Spot.AiSearch.Api.Tests.Services.Gemini;

public sealed class GeminiClientTests
{
    private static GeminiOptions Options => new()
    {
        ApiKey = "test-api-key",
        Model = "gemini-test",
        BaseUrl = "https://fake.googleapis.com/v1beta",
        TimeoutSeconds = 5,
    };

    private static GeminiClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        out FakeHttpMessageHandler fakeHandler,
        GeminiOptions? options = null)
    {
        fakeHandler = new FakeHttpMessageHandler(handler);
        var httpClient = new HttpClient(fakeHandler);
        return new GeminiClient(httpClient, Microsoft.Extensions.Options.Options.Create(options ?? Options), NullLogger<GeminiClient>.Instance);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_SuccessfulResponse_ReturnsParsedCriteriaAndUsage()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        role = "model",
                        parts = new[] { new { text = """{"serviceQuery":"corte de cabello","date":"2026-08-31","time":"14:00:00","budgetMaxColones":15000}""" } },
                    },
                },
            },
            usageMetadata = new { promptTokenCount = 50, candidatesTokenCount = 20, totalTokenCount = 70 },
        })), out _);

        var result = await client.ExtractSearchCriteriaAsync("Busco un corte de cabello mañana en la tarde");

        Assert.Equal(AiRequestStatus.SUCCESS, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.ExtractedParameters);
        Assert.Equal("corte de cabello", result.ExtractedParameters!.RootElement.GetProperty("serviceQuery").GetString());
        Assert.Equal(50, result.InputTokens);
        Assert.Equal(20, result.OutputTokens);
        Assert.Equal(70, result.TotalTokens);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_SendsApiKeyAsHeader_NotInUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
            {
                candidates = new[]
                {
                    new { content = new { role = "model", parts = new[] { new { text = "{}" } } } },
                },
            }));
        }, out _);

        await client.ExtractSearchCriteriaAsync("cualquier consulta");

        Assert.NotNull(capturedRequest);
        Assert.Equal("test-api-key", capturedRequest!.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("key=", capturedRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_SendsExpectedRequestBodyShape()
    {
        string? capturedBody = null;
        var client = CreateClient(async (request, _) =>
        {
            // Read the body here, not after SendAsync returns: GeminiClient disposes its
            // HttpRequestMessage (and its Content) once the response comes back.
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, new
            {
                candidates = new[]
                {
                    new { content = new { role = "model", parts = new[] { new { text = "{}" } } } },
                },
            });
        }, out _);

        await client.ExtractSearchCriteriaAsync("Busco un corte de cabello mañana en la tarde");

        Assert.NotNull(capturedBody);
        using var json = JsonDocument.Parse(capturedBody!);
        var root = json.RootElement;

        // Tuned deliberately (see GeminiApiModels.cs) to disable multi-step reasoning for what is
        // a plain extraction task — a regression here silently reintroduces the token/latency cost.
        Assert.Equal(0, root.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
        Assert.True(root.GetProperty("generationConfig").TryGetProperty("responseSchema", out var responseSchema));
        Assert.Equal("OBJECT", responseSchema.GetProperty("type").GetString());
        Assert.True(responseSchema.GetProperty("properties").TryGetProperty("serviceQuery", out _));

        var systemInstructionText = root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString();
        var expectedToday = DateOnly.FromDateTime(DateTime.UtcNow + TimeSpan.FromHours(-6));
        Assert.Contains(expectedToday.ToString("yyyy-MM-dd"), systemInstructionText);

        var userText = root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal("Busco un corte de cabello mañana en la tarde", userText);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_ApiReturnsErrorStatus_MapsToErrorStatus()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Forbidden, new
        {
            error = new { code = 403, status = "PERMISSION_DENIED", message = "API key not valid." },
        })), out _);

        var result = await client.ExtractSearchCriteriaAsync("cualquier consulta");

        Assert.Equal(AiRequestStatus.ERROR, result.Status);
        Assert.Equal("GEMINI_API_ERROR", result.ErrorCode);
        Assert.Equal("API key not valid.", result.ErrorMessage);
        Assert.Null(result.ExtractedParameters);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_ModelReturnsNonJsonText_MapsToInvalidResponseError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new
        {
            candidates = new[]
            {
                new { content = new { role = "model", parts = new[] { new { text = "not valid json" } } } },
            },
        })), out _);

        var result = await client.ExtractSearchCriteriaAsync("cualquier consulta");

        Assert.Equal(AiRequestStatus.ERROR, result.Status);
        Assert.Equal("GEMINI_INVALID_RESPONSE", result.ErrorCode);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_ResponseEnvelopeIsNotJson_MapsToMalformedResponseError()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("this is not json at all"),
        }), out _);

        var result = await client.ExtractSearchCriteriaAsync("cualquier consulta");

        Assert.Equal(AiRequestStatus.ERROR, result.Status);
        Assert.Equal("GEMINI_MALFORMED_RESPONSE", result.ErrorCode);
        Assert.Null(result.ExtractedParameters);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_RequestExceedsTimeout_MapsToTimeoutStatus()
    {
        var options = Options;
        options.TimeoutSeconds = 1;

        var client = CreateClient(async (_, ct) =>
        {
            // Never completes within the configured timeout; honors cancellation like a real
            // stalled HTTP call would.
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return JsonResponse(HttpStatusCode.OK, new { });
        }, out _, options);

        var result = await client.ExtractSearchCriteriaAsync("cualquier consulta");

        Assert.Equal(AiRequestStatus.TIMEOUT, result.Status);
        Assert.Equal("GEMINI_TIMEOUT", result.ErrorCode);
    }

    [Fact]
    public async Task ExtractSearchCriteriaAsync_CallerCancelsBeforeTimeout_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var client = CreateClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return JsonResponse(HttpStatusCode.OK, new { });
        }, out _);

        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExtractSearchCriteriaAsync("cualquier consulta", cts.Token));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) =>
        new(statusCode) { Content = JsonContent.Create(body) };
}
