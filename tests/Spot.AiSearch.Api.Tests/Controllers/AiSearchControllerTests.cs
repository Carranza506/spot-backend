using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Services.Business;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.AiSearch.Api.Tests.Fakes;

namespace Spot.AiSearch.Api.Tests.Controllers;

[Collection(AiSearchApiCollection.Name)]
public class AiSearchControllerTests : IClassFixture<AiSearchApiFactory>
{
    private const string Url = "/ai-search/query";
    private const string GenericErrorMessage = "Ocurrió un error inesperado. Por favor intenta de nuevo más tarde.";

    private readonly AiSearchApiFactory _factory;

    public AiSearchControllerTests(AiSearchApiFactory factory)
    {
        _factory = factory;
        _factory.Gemini.Reset();
        _factory.BusinessSearch.Reset();
    }

    // ---------- Auth ----------

    [Fact]
    public async Task Query_WithoutToken_Returns401WithErrorBody()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(Url, new { query = "corte de cabello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHORIZED", body.GetProperty("code").GetString());
        Assert.Null(_factory.Gemini.LastQuery);
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("BUSINESS")]
    [InlineData("SUPERADMIN")]
    public async Task Query_AnyAuthenticatedRole_Returns200(string role)
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"serviceQuery":"corte de cabello"}""");

        var response = await CreateClient(role).PostAsJsonAsync(Url, new { query = "corte de cabello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- 200 ----------

    [Fact]
    public async Task Query_ReturnsCriteriaBusinessesAndAiRequestInContractShape()
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success(
            """{"serviceQuery":"corte de cabello","date":"2026-08-31","time":"14:00:00","budgetMaxColones":15000,"rawLocationText":"Sarapiquí"}""");
        var business = SampleBusiness();
        _factory.BusinessSearch.Businesses.Add(business);

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new
        {
            query = "Busco un corte de cabello mañana en la tarde, 15 mil, cerca de Sarapiquí",
            location = new { latitude = 10.4653, longitude = -84.0068 },
            radiusKm = 15,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var criteria = body.GetProperty("criteria");
        Assert.Equal("corte de cabello", criteria.GetProperty("serviceQuery").GetString());
        Assert.Equal("2026-08-31", criteria.GetProperty("date").GetString());
        Assert.Equal("14:00:00", criteria.GetProperty("time").GetString());
        Assert.Equal(15000m, criteria.GetProperty("budgetMaxColones").GetDecimal());
        Assert.Equal("Sarapiquí", criteria.GetProperty("rawLocationText").GetString());
        // Not resolved in this PR: null, so left out.
        Assert.False(criteria.TryGetProperty("categoryId", out _));

        var businesses = body.GetProperty("businesses");
        var first = Assert.Single(businesses.GetProperty("data").EnumerateArray());
        Assert.Equal(business.Id, first.GetProperty("id").GetGuid());
        Assert.Equal("Salón Bella Vista", first.GetProperty("name").GetString());
        Assert.Equal("salon-bella-vista", first.GetProperty("slug").GetString());
        Assert.True(first.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, businesses.GetProperty("pagination").GetProperty("total").GetInt32());
        Assert.Equal(1, businesses.GetProperty("pagination").GetProperty("page").GetInt32());
        Assert.Equal(20, businesses.GetProperty("pagination").GetProperty("pageSize").GetInt32());

        var aiRequest = body.GetProperty("aiRequest");
        var row = _factory.Repository.All[^1];
        Assert.Equal(row.Id, aiRequest.GetProperty("id").GetGuid());
        Assert.Equal("SUCCESS", aiRequest.GetProperty("status").GetString());
        Assert.Equal("BUSINESS_SEARCH", aiRequest.GetProperty("requestType").GetString());
        Assert.Equal(60, aiRequest.GetProperty("totalTokens").GetInt32());
        Assert.Equal(300, aiRequest.GetProperty("latencyMs").GetInt32());
        Assert.True(aiRequest.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Query_AiRequest_HasExactlyTheAiRequestSummaryKeys()
    {
        // Any role can call this endpoint: nothing else from the ai_requests row (prompt, model
        // response, tokens split, IP, user agent, user id, error fields) may leak through aiRequest.
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"serviceQuery":"corte de cabello"}""");

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "corte de cabello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var aiRequest = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("aiRequest");
        Assert.Equal(
            ["createdAt", "id", "latencyMs", "requestType", "status", "totalTokens"],
            aiRequest.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Query_SendsServiceQueryLocationAndPagingToBusiness_NotDateTimeOrBudget()
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success(
            """{"serviceQuery":"manicura","date":"2026-08-31","time":"14:00:00","budgetMaxColones":8000}""");

        await CreateClient("CLIENT").PostAsJsonAsync(Url, new
        {
            query = "manicura barata el lunes",
            location = new { latitude = 9.9333, longitude = -84.0833 },
            radiusKm = 5,
            page = 2,
            pageSize = 10,
        });

        // BusinessSearchRequest has no date, time or budget field: those can only reach the response.
        Assert.Equal(
            new BusinessSearchRequest("manicura", null, 9.9333, -84.0833, 5, 2, 10),
            _factory.BusinessSearch.LastRequest);
    }

    [Fact]
    public async Task Query_WithoutLocation_SendsNoProximityAndUsesDefaultPaging()
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"serviceQuery":"manicura"}""");

        // radiusKm alone is ignored: proximity needs a location.
        await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "manicura", radiusKm = 30 });

        Assert.Equal(
            new BusinessSearchRequest("manicura", null, null, null, null, 1, 20),
            _factory.BusinessSearch.LastRequest);
    }

    [Fact]
    public async Task Query_OnlyALocation_SearchesByProximityWithoutText()
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"date":"2026-08-31"}""");

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new
        {
            query = "algo para mañana cerca de aquí",
            location = new { latitude = 9.9333, longitude = -84.0833 },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new BusinessSearchRequest(null, null, 9.9333, -84.0833, 10, 1, 20),
            _factory.BusinessSearch.LastRequest);
    }

    [Fact]
    public async Task Query_RecordsTheCallWithTheTrimmedQuery()
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"serviceQuery":"barbería"}""");

        await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "   barbería en Heredia   " });

        Assert.Equal("barbería en Heredia", _factory.Gemini.LastQuery);
        var row = _factory.Repository.All[^1];
        Assert.Equal("barbería en Heredia", row.Prompt);
        Assert.Equal(AiRequestStatus.SUCCESS, row.Status);
        Assert.Equal(AiRequestType.BUSINESS_SEARCH, row.RequestType);
        Assert.NotNull(row.UserId);
        Assert.Null(row.ErrorCode);
    }

    [Fact]
    public async Task Query_InvalidCriteriaFromTheModel_AreLeftOut()
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success(
            """{"serviceQuery":"corte","date":"2026-13-45","time":"25:00:00","budgetMaxColones":-5,"rawLocationText":"   "}""");

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "corte el día 45" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var criteria = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("criteria");
        Assert.Equal("corte", criteria.GetProperty("serviceQuery").GetString());
        Assert.False(criteria.TryGetProperty("date", out _));
        Assert.False(criteria.TryGetProperty("time", out _));
        Assert.False(criteria.TryGetProperty("budgetMaxColones", out _));
        Assert.False(criteria.TryGetProperty("rawLocationText", out _));
    }

    // ---------- 422 ----------

    [Fact]
    public async Task Query_NothingUsable_Returns422_RecordsWhyInTheAudit_AndNeverSearches()
    {
        // Date and budget alone can't narrow a business search.
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"date":"2026-08-31","budgetMaxColones":5000}""");
        var rowsBefore = _factory.Repository.All.Count;

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "algo para mañana" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AI_SEARCH_NOT_UNDERSTOOD", body.GetProperty("code").GetString());
        Assert.Equal(rowsBefore + 1, _factory.Repository.All.Count);
        var row = _factory.Repository.All[^1];
        // status describes the Gemini call (it answered); why the search didn't run is in error_code.
        Assert.Equal(AiRequestStatus.SUCCESS, row.Status);
        Assert.Equal("AI_SEARCH_NOT_UNDERSTOOD", row.ErrorCode);
        Assert.NotNull(row.ErrorMessage);
        Assert.Null(_factory.BusinessSearch.LastRequest);
    }

    // ---------- 500 ----------

    [Fact]
    public async Task Query_GeminiError_Returns500Generic_RecordsTheError_AndNeverSearches()
    {
        _factory.Gemini.Respond = _ => GeminiExtractionResult.Error(
            latencyMs: 80, errorCode: "GEMINI_API_ERROR", errorMessage: "API key not valid. Please pass a valid API key.");

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "corte de cabello" });

        await AssertGenericServerErrorAsync(response, "API key not valid");
        var row = _factory.Repository.All[^1];
        Assert.Equal(AiRequestStatus.ERROR, row.Status);
        Assert.Equal("GEMINI_API_ERROR", row.ErrorCode);
        Assert.Null(_factory.BusinessSearch.LastRequest);
    }

    [Fact]
    public async Task Query_GeminiTimeout_Returns500Generic_AndRecordsTheTimeout()
    {
        _factory.Gemini.Respond = _ => GeminiExtractionResult.Timeout(10000, "Gemini API call timed out after 10s.");

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "corte de cabello" });

        await AssertGenericServerErrorAsync(response, "timed out");
        Assert.Equal(AiRequestStatus.TIMEOUT, _factory.Repository.All[^1].Status);
    }

    [Fact]
    public async Task Query_BusinessDown_Returns500Generic_AndRecordsTheBusinessFailure()
    {
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"serviceQuery":"corte de cabello"}""");
        _factory.BusinessSearch.FailWith = new BusinessSearchUnavailableException("Business search call failed: connection to 10.0.0.5 refused.");
        var rowsBefore = _factory.Repository.All.Count;

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "corte de cabello" });

        await AssertGenericServerErrorAsync(response, "10.0.0.5");
        Assert.Equal(rowsBefore + 1, _factory.Repository.All.Count);
        var row = _factory.Repository.All[^1];
        Assert.Equal(AiRequestStatus.SUCCESS, row.Status);
        Assert.Equal("BUSINESS_SEARCH_FAILED", row.ErrorCode);
        // The detail stays in the audit row (SUPERADMIN only), never in the response.
        Assert.Contains("10.0.0.5", row.ErrorMessage);
    }

    [Fact]
    public async Task Query_UnexpectedFailureDuringTheSearch_StillRecordsTheCall()
    {
        // Gemini already answered (and was billed): the call is audited even when the search ends
        // some other way than a Business failure.
        _factory.Gemini.Respond = _ => FakeGeminiClient.Success("""{"serviceQuery":"corte de cabello"}""");
        _factory.BusinessSearch.FailWith = new InvalidOperationException("unexpected failure at 10.0.0.9");
        var rowsBefore = _factory.Repository.All.Count;

        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = "corte de cabello" });

        await AssertGenericServerErrorAsync(response, "10.0.0.9");
        Assert.Equal(rowsBefore + 1, _factory.Repository.All.Count);
        Assert.Equal(AiRequestStatus.SUCCESS, _factory.Repository.All[^1].Status);
    }

    // ---------- 400 ----------

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"query":null}""")]
    [InlineData("""{"query":"ab"}""")]
    [InlineData("""{"query":"     "}""")]
    [InlineData("""{"query":"  a  "}""")]
    [InlineData("""{"query":"corte","radiusKm":0}""")]
    [InlineData("""{"query":"corte","radiusKm":100.5}""")]
    [InlineData("""{"query":"corte","radiusKm":null}""")]
    [InlineData("""{"query":"corte","page":0}""")]
    [InlineData("""{"query":"corte","pageSize":0}""")]
    [InlineData("""{"query":"corte","pageSize":101}""")]
    [InlineData("""{"query":"corte","location":{"latitude":91,"longitude":-84}}""")]
    [InlineData("""{"query":"corte","location":{"latitude":9.9}}""")]
    [InlineData("""{"query":"corte","location":{"latitude":9.9,"longitude":-181}}""")]
    [InlineData("""{"query":""")]
    public async Task Query_InvalidBody_Returns400InTheErrorShape_WithoutCallingGemini(string json)
    {
        var response = await CreateClient("CLIENT").PostAsync(Url, new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Null(_factory.Gemini.LastQuery);
    }

    [Fact]
    public async Task Query_LongerThan500Characters_Returns400()
    {
        var response = await CreateClient("CLIENT").PostAsJsonAsync(Url, new { query = new string('a', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_factory.Gemini.LastQuery);
    }

    private HttpClient CreateClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.IssueAccessToken(role));
        return client;
    }

    /// <summary>The contract's InternalServerError body, with nothing of what actually failed in it.</summary>
    private static async Task AssertGenericServerErrorAsync(HttpResponseMessage response, string leakedDetail)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("INTERNAL_ERROR", body.GetProperty("code").GetString());
        Assert.Equal(GenericErrorMessage, body.GetProperty("message").GetString());
        Assert.False(body.TryGetProperty("details", out _));
        Assert.DoesNotContain(leakedDetail, raw);
    }

    private static BusinessDto SampleBusiness() => new(
        Id: Guid.NewGuid(),
        Name: "Salón Bella Vista",
        Slug: "salon-bella-vista",
        Description: null,
        LegalName: null,
        Email: null,
        Phone: null,
        Website: null,
        LogoUrl: null,
        IsActive: true,
        CreatedAt: new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
        UpdatedAt: new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero));
}
