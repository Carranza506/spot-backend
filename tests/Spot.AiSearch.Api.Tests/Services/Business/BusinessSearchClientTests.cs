using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Spot.AiSearch.Api.Options;
using Spot.AiSearch.Api.Services.Business;
using Spot.AiSearch.Api.Tests.TestSupport;

namespace Spot.AiSearch.Api.Tests.Services.Business;

public sealed class BusinessSearchClientTests
{
    private static BusinessApiOptions Options => new()
    {
        BaseUrl = "http://business-api:8080/",
        TimeoutSeconds = 5,
    };

    private static BusinessSearchClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        out FakeHttpMessageHandler fakeHandler,
        BusinessApiOptions? options = null)
    {
        fakeHandler = new FakeHttpMessageHandler(handler);
        var httpClient = new HttpClient(fakeHandler);
        return new BusinessSearchClient(
            httpClient, Microsoft.Extensions.Options.Options.Create(options ?? Options), NullLogger<BusinessSearchClient>.Instance);
    }

    private static readonly object EmptyPage = new
    {
        data = Array.Empty<object>(),
        pagination = new { page = 1, pageSize = 20, total = 0, totalPages = 0 },
    };

    [Fact]
    public async Task SearchAsync_SendsEveryFilterToTheBusinessSearchEndpoint()
    {
        var categoryId = Guid.NewGuid();
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, EmptyPage)), out var handler);

        await client.SearchAsync(new BusinessSearchRequest("corte de cabello", categoryId, 9.9333, -84.0833, 5.5, 2, 10));

        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        // Business itself, not the Gateway (no /api/v1 prefix), with the base URL's trailing slash trimmed.
        Assert.Equal("http://business-api:8080/business/businesses", uri.GetLeftPart(UriPartial.Path));
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("corte de cabello", query["q"]);
        Assert.Equal(categoryId.ToString(), query["categoryId"]);
        Assert.Equal("9.9333", query["lat"]);
        Assert.Equal("-84.0833", query["lng"]);
        Assert.Equal("5.5", query["radiusKm"]);
        Assert.Equal("2", query["page"]);
        Assert.Equal("10", query["pageSize"]);
    }

    [Fact]
    public async Task SearchAsync_HostileServiceQuery_ArrivesAsASingleEncodedQ()
    {
        // A prompt-injected serviceQuery must not add, override or smuggle filters into the URL.
        const string hostile = "corte&categoryId=11111111-1111-1111-1111-111111111111&lat=0&lng=0&radiusKm=100&page=9";
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, EmptyPage)), out var handler);

        await client.SearchAsync(new BusinessSearchRequest(hostile, null, null, null, null, 1, 20));

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(handler.LastRequest!.RequestUri!.Query);
        Assert.Equal(["page", "pageSize", "q"], query.Keys.Order());
        Assert.Equal(hostile, query["q"]);
        Assert.Equal("1", query["page"]);
        Assert.Equal("20", query["pageSize"]);
    }

    [Fact]
    public async Task SearchAsync_SendsNoAuthorizationHeader()
    {
        // Business search is public: the caller's token must never be forwarded to another service.
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, EmptyPage)), out var handler);

        await client.SearchAsync(new BusinessSearchRequest("corte", null, null, null, null, 1, 20));

        Assert.Null(handler.LastRequest!.Headers.Authorization);
        Assert.False(handler.LastRequest.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task SearchAsync_NumbersUseADecimalPoint_WhateverTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-CR");
        try
        {
            var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, EmptyPage)), out var handler);

            await client.SearchAsync(new BusinessSearchRequest(null, null, 9.9333, -84.0833, 2.5, 1, 20));

            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(handler.LastRequest!.RequestUri!.Query);
            Assert.Equal("9.9333", query["lat"]);
            Assert.Equal("2.5", query["radiusKm"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(9.9333, null, 5.0)]
    [InlineData(9.9333, -84.0833, null)]
    public async Task SearchAsync_IncompleteProximity_SendsNoneOfIt(double? lat, double? lng, double? radiusKm)
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, EmptyPage)), out var handler);

        await client.SearchAsync(new BusinessSearchRequest(null, null, lat, lng, radiusKm, 1, 20));

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(handler.LastRequest!.RequestUri!.Query);
        Assert.False(query.ContainsKey("lat"));
        Assert.False(query.ContainsKey("lng"));
        Assert.False(query.ContainsKey("radiusKm"));
        Assert.False(query.ContainsKey("q"));
        Assert.False(query.ContainsKey("categoryId"));
    }

    [Fact]
    public async Task SearchAsync_ParsesTheBusinessPage()
    {
        var id = Guid.NewGuid();
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new
        {
            data = new[]
            {
                new
                {
                    id,
                    name = "Salón Bella Vista",
                    slug = "salon-bella-vista",
                    description = (string?)null,
                    legalName = (string?)null,
                    email = "contacto@bellavista.cr",
                    phone = (string?)null,
                    website = (string?)null,
                    logoUrl = (string?)null,
                    isActive = true,
                    createdAt = "2026-01-10T09:00:00.123456+00:00",
                    updatedAt = "2026-01-10T09:00:00.123456+00:00",
                },
            },
            pagination = new { page = 1, pageSize = 20, total = 1, totalPages = 1 },
        })), out _);

        var page = await client.SearchAsync(new BusinessSearchRequest("salón", null, null, null, null, 1, 20));

        var business = Assert.Single(page.Data);
        Assert.Equal(id, business.Id);
        Assert.Equal("Salón Bella Vista", business.Name);
        Assert.Equal("contacto@bellavista.cr", business.Email);
        Assert.Null(business.Description);
        Assert.Equal(1, page.Pagination.Total);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SearchAsync_ErrorStatus_Throws(HttpStatusCode statusCode)
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(statusCode, new { code = "X", message = "y" })), out _);

        await Assert.ThrowsAsync<BusinessSearchUnavailableException>(
            () => client.SearchAsync(new BusinessSearchRequest("corte", null, null, null, null, 1, 20)));
    }

    [Fact]
    public async Task SearchAsync_BusinessUnreachable_Throws()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused (business-api:8080)"), out _);

        await Assert.ThrowsAsync<BusinessSearchUnavailableException>(
            () => client.SearchAsync(new BusinessSearchRequest("corte", null, null, null, null, 1, 20)));
    }

    [Theory]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("null")]
    public async Task SearchAsync_BodyIsNotABusinessPage_Throws(string body)
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        }), out _);

        await Assert.ThrowsAsync<BusinessSearchUnavailableException>(
            () => client.SearchAsync(new BusinessSearchRequest("corte", null, null, null, null, 1, 20)));
    }

    [Fact]
    public async Task SearchAsync_RequestExceedsTimeout_Throws()
    {
        var options = Options;
        options.TimeoutSeconds = 1;

        var client = CreateClient(async (_, ct) =>
        {
            // Never completes within the configured timeout; honors cancellation like a real
            // stalled HTTP call would.
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return JsonResponse(HttpStatusCode.OK, EmptyPage);
        }, out _, options);

        await Assert.ThrowsAsync<BusinessSearchUnavailableException>(
            () => client.SearchAsync(new BusinessSearchRequest("corte", null, null, null, null, 1, 20)));
    }

    [Fact]
    public async Task SearchAsync_CallerCancelsBeforeTimeout_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var client = CreateClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return JsonResponse(HttpStatusCode.OK, EmptyPage);
        }, out _);

        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchAsync(new BusinessSearchRequest("corte", null, null, null, null, 1, 20), cts.Token));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) =>
        new(statusCode) { Content = JsonContent.Create(body) };
}
