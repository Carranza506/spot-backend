using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.Tests.Controllers;

[Collection(AiSearchApiCollection.Name)]
public class AiRequestsControllerTests(AiSearchApiFactory factory) : IClassFixture<AiSearchApiFactory>
{
    // ---------- GET /ai-search/requests/{aiRequestId} ----------

    [Fact]
    public async Task GetAiRequest_AsSuperadmin_ReturnsTheFullLogInContractShape()
    {
        var row = await SeedAsync();

        var response = await CreateClient("SUPERADMIN").GetAsync($"/ai-search/requests/{row.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(row.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(row.UserId, body.GetProperty("userId").GetGuid());
        Assert.Equal("BUSINESS_SEARCH", body.GetProperty("requestType").GetString());
        Assert.Equal("SUCCESS", body.GetProperty("status").GetString());
        Assert.Equal("Busco barbería en Heredia", body.GetProperty("prompt").GetString());
        // JSON columns come back as JSON, not as escaped strings.
        Assert.Equal("Heredia", body.GetProperty("extractedParameters").GetProperty("city").GetString());
        Assert.Equal("searchBusinesses", body.GetProperty("toolCalls")[0].GetProperty("name").GetString());
        Assert.Equal("203.0.113.7", body.GetProperty("ipAddress").GetString());
    }

    [Fact]
    public async Task GetAiRequest_UnknownId_Returns404WithErrorBody()
    {
        await SeedAsync();

        var response = await CreateClient("SUPERADMIN").GetAsync($"/ai-search/requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
        Assert.Equal("La interacción de IA indicada no existe.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetAiRequest_MalformedId_Returns404()
    {
        var response = await CreateClient("SUPERADMIN").GetAsync("/ai-search/requests/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAiRequest_WithoutToken_Returns401WithErrorBody()
    {
        var row = await SeedAsync();

        var response = await factory.CreateClient().GetAsync($"/ai-search/requests/{row.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHORIZED", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("BUSINESS")]
    public async Task GetAiRequest_NonSuperadmin_Returns403(string role)
    {
        // An existing id: the role check must win, so prompts never leak to other roles.
        var row = await SeedAsync();

        var response = await CreateClient(role).GetAsync($"/ai-search/requests/{row.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- GET /ai-search/requests ----------

    [Fact]
    public async Task ListAiRequests_PageWhoseOffsetOverflowsInt_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        // (30,000,000 - 1) * 100 overflows int; it used to become a negative OFFSET and a 500.
        var response = await CreateClient("SUPERADMIN").GetAsync("/ai-search/requests?page=30000000&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("data").GetArrayLength());
        Assert.True(body.GetProperty("pagination").GetProperty("total").GetInt32() >= 1);
    }

    private HttpClient CreateClient(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(role));
        return client;
    }

    private async Task<AiRequest> SeedAsync()
    {
        var row = new AiRequest
        {
            UserId = Guid.NewGuid(),
            Provider = "gemini",
            Model = "gemini-test",
            RequestType = AiRequestType.BUSINESS_SEARCH,
            Prompt = "Busco barbería en Heredia",
            ExtractedParameters = JsonDocument.Parse("""{"city":"Heredia"}"""),
            ToolCalls = JsonDocument.Parse("""[{"name":"searchBusinesses"}]"""),
            Status = AiRequestStatus.SUCCESS,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            CreatedAt = DateTime.UtcNow,
        };
        await factory.Repository.AddAsync(row);
        return row;
    }
}
