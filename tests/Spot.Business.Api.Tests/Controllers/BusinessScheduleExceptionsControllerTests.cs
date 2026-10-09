using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Business.Api.Models;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Controllers;

[Collection(ApiFactoryCollection.Name)]
public class BusinessScheduleExceptionsControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    // ---------- GET /business/businesses/{businessId}/schedule-exceptions ----------

    [Fact]
    public async Task ListExceptions_NoToken_Returns200WithOnlyThatBusinessesExceptionsByDateAscending()
    {
        var (business, _) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        SeedException(business.Id, "2026-12-31");
        factory.ScheduleExceptionRepository.Seed(new BusinessScheduleException
        {
            BusinessId = business.Id,
            ExceptionDate = new DateOnly(2026, 12, 24),
            OpenTime = new TimeOnly(9, 0),
            CloseTime = new TimeOnly(13, 0),
            Reason = "Nochebuena",
        });
        SeedException(other.Id, "2026-12-01");
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/schedule-exceptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.Equal(["2026-12-24", "2026-12-31"], Dates(body));
        var first = data[0];
        Assert.Equal(business.Id, first.GetProperty("businessId").GetGuid());
        Assert.False(first.GetProperty("isClosed").GetBoolean());
        Assert.Equal("09:00:00", first.GetProperty("openTime").GetString());
        Assert.Equal("13:00:00", first.GetProperty("closeTime").GetString());
        Assert.Equal("Nochebuena", first.GetProperty("reason").GetString());
        Assert.True(first.TryGetProperty("createdAt", out _));
        Assert.True(first.TryGetProperty("updatedAt", out _));
        Assert.Equal(JsonValueKind.Null, data[1].GetProperty("openTime").ValueKind);
        Assert.Equal(2, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("from=2026-12-24", new[] { "2026-12-24", "2026-12-25", "2026-12-31" })]
    [InlineData("to=2026-12-25", new[] { "2026-12-01", "2026-12-24", "2026-12-25" })]
    [InlineData("from=2026-12-24&to=2026-12-25", new[] { "2026-12-24", "2026-12-25" })]
    [InlineData("from=2026-12-25&to=2026-12-25", new[] { "2026-12-25" })]
    [InlineData("from=2027-01-01", new string[0])]
    public async Task ListExceptions_FromTo_FiltersInclusiveAtBothEnds(string queryString, string[] expected)
    {
        var (business, _) = Reset();
        foreach (var date in new[] { "2026-12-01", "2026-12-24", "2026-12-25", "2026-12-31" })
            SeedException(business.Id, date);
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/business/businesses/{business.Id}/schedule-exceptions?{queryString}");

        Assert.Equal(expected, Dates(body));
        Assert.Equal(expected.Length, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListExceptions_FromAfterTo_Returns400()
    {
        var (business, _) = Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/business/businesses/{business.Id}/schedule-exceptions?from=2026-12-26&to=2026-12-25");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("from=2026/12/25")]
    [InlineData("from=25-12-2026")]
    [InlineData("from=12/25/2026")]
    [InlineData("from=2026-12-5")]
    [InlineData("from=2026-12-25T00:00:00")]
    [InlineData("from=%202026-12-25")]
    [InlineData("to=2026-02-30")]
    [InlineData("to=hoy")]
    public async Task ListExceptions_InvalidDateFormat_Returns400(string queryString)
    {
        var (business, _) = Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/schedule-exceptions?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListExceptions_Pagination_Page1And2AreDisjoint()
    {
        var (business, _) = Reset();
        foreach (var date in new[] { "2026-12-03", "2026-12-01", "2026-12-02" })
            SeedException(business.Id, date);
        var client = factory.CreateClient();

        var page1 = await client.GetFromJsonAsync<JsonElement>(
            $"/business/businesses/{business.Id}/schedule-exceptions?page=1&pageSize=2");
        var page2 = await client.GetFromJsonAsync<JsonElement>(
            $"/business/businesses/{business.Id}/schedule-exceptions?page=2&pageSize=2");

        Assert.Equal(["2026-12-01", "2026-12-02"], Dates(page1));
        Assert.Equal(["2026-12-03"], Dates(page2));
        var pagination = page2.GetProperty("pagination");
        Assert.Equal(2, pagination.GetProperty("page").GetInt32());
        Assert.Equal(2, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, pagination.GetProperty("total").GetInt32());
        Assert.Equal(2, pagination.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task ListExceptions_InvalidPagination_Returns400(string queryString)
    {
        var (business, _) = Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/schedule-exceptions?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListExceptions_UnknownBusiness_Returns404()
    {
        Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{Guid.NewGuid()}/schedule-exceptions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListExceptions_InactiveBusiness_Returns404()
    {
        var (business, _) = Reset();
        business.IsActive = false;
        SeedException(business.Id, "2026-12-25");
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/schedule-exceptions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- POST /business/businesses/{businessId}/schedule-exceptions ----------

    [Fact]
    public async Task CreateException_OwnerOpenDay_Returns201WithTheException()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-24", isClosed = false, openTime = "09:00:00", closeTime = "13:00:00", reason = "  Nochebuena  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("id", out _));
        Assert.Equal(business.Id, body.GetProperty("businessId").GetGuid());
        Assert.Equal("2026-12-24", body.GetProperty("exceptionDate").GetString());
        Assert.False(body.GetProperty("isClosed").GetBoolean());
        Assert.Equal("09:00:00", body.GetProperty("openTime").GetString());
        Assert.Equal("13:00:00", body.GetProperty("closeTime").GetString());
        Assert.Equal("Nochebuena", body.GetProperty("reason").GetString());
        Assert.True(body.TryGetProperty("createdAt", out _));
        Assert.True(body.TryGetProperty("updatedAt", out _));

        var stored = Assert.Single(factory.ScheduleExceptionRepository.All);
        Assert.Equal(new TimeOnly(9, 0), stored.OpenTime);
        Assert.Equal("Nochebuena", stored.Reason);
    }

    [Fact]
    public async Task CreateException_OwnerClosedDay_Returns201()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true, reason = "Feriado de Navidad" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isClosed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("openTime").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("closeTime").ValueKind);
        Assert.Equal("Feriado de Navidad", body.GetProperty("reason").GetString());
        Assert.Single(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_ClosedDayWithTimes_StoresNullTimes()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        // The times would even break INVALID_TIME_RANGE: on a closed day they're ignored entirely.
        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true, openTime = "18:00:00", closeTime = "09:00:00" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("openTime").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("closeTime").ValueKind);
        var stored = Assert.Single(factory.ScheduleExceptionRepository.All);
        Assert.Null(stored.OpenTime);
        Assert.Null(stored.CloseTime);
    }

    [Theory]
    [InlineData("{\"exceptionDate\":\"2026-12-25\",\"isClosed\":true,\"openTime\":null,\"closeTime\":null,\"reason\":null}")]
    [InlineData("{\"exceptionDate\":\"2026-12-25\",\"isClosed\":true,\"reason\":\"   \"}")]
    [InlineData("{\"exceptionDate\":\"2026-12-25\",\"isClosed\":true}")]
    public async Task CreateException_NullOrBlankOptionalFields_Returns201WithNullReason(string json)
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reason").ValueKind);
        Assert.Null(Assert.Single(factory.ScheduleExceptionRepository.All).Reason);
    }

    [Fact]
    public async Task CreateException_PastDate_Returns201()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2020-01-01", isClosed = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateException_OwnerOfInactiveBusiness_Returns201()
    {
        var (business, ownerId) = Reset();
        business.IsActive = false;
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_NonOwnerBusinessAccount_Returns403()
    {
        var (business, _) = Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_ClientRole_Returns403()
    {
        // Even the business's own account id: the CLIENT role alone is rejected by [Authorize(Roles)].
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "CLIENT");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_NoToken_Returns401()
    {
        var (business, _) = Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_UnknownBusiness_Returns404()
    {
        Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{Guid.NewGuid()}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_DuplicateDate_Returns409AndKeepsTheOriginal()
    {
        var (business, ownerId) = Reset();
        var original = SeedException(business.Id, "2026-12-25");
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = false, openTime = "09:00:00", closeTime = "12:00:00" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SCHEDULE_EXCEPTION_ALREADY_EXISTS", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("message", out _));
        Assert.True(body.TryGetProperty("timestamp", out _));
        var stored = Assert.Single(factory.ScheduleExceptionRepository.All);
        Assert.Equal(original.Id, stored.Id);
        Assert.True(stored.IsClosed);
    }

    [Fact]
    public async Task CreateException_SameDateOnAnotherBusiness_Returns201()
    {
        var (business, ownerId) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        SeedException(other.Id, "2026-12-25");
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    // Times: same HH:mm:ss pattern as #60.
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"09:00\",\"closeTime\":\"13:00:00\"}")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"9:00:00\",\"closeTime\":\"13:00:00\"}")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"09:00:00.5\",\"closeTime\":\"13:00:00\"}")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":\"24:00:00\"}")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":true,\"openTime\":\"9am\"}")]
    // exceptionDate: System.Text.Json's DateOnly only reads exactly YYYY-MM-DD.
    [InlineData("{\"exceptionDate\":\"2026/12/25\",\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":\"25-12-2026\",\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":\"12/25/2026\",\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":\"2026-12-5\",\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":\"2026-12-25T00:00:00\",\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":\" 2026-12-25\",\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":\"2026-02-30\",\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":20261225,\"isClosed\":true}")]
    // Required fields.
    [InlineData("{\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":null,\"isClosed\":true}")]
    [InlineData("{\"exceptionDate\":\"2026-12-25\"}")]
    [InlineData("{\"exceptionDate\":\"2026-12-25\",\"isClosed\":null}")]
    public async Task CreateException_InvalidFormatOrMissingField_Returns400(string json)
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_ReasonOver255_Returns400()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true, reason = new string('a', 256) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task CreateException_Reason255_Returns201()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new { exceptionDate = "2026-12-25", isClosed = true, reason = new string('a', 255) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false}", "MISSING_OPENING_HOURS")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"09:00:00\"}", "MISSING_OPENING_HOURS")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":null}", "MISSING_OPENING_HOURS")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"18:00:00\",\"closeTime\":\"09:00:00\"}", "INVALID_TIME_RANGE")]
    [InlineData("{\"exceptionDate\":\"2026-12-24\",\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":\"09:00:00\"}", "INVALID_TIME_RANGE")]
    public async Task CreateException_BrokenOpeningHoursRule_Returns422WithTheRuleCode(string json, string expectedCode)
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsync($"/business/businesses/{business.Id}/schedule-exceptions",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("message", out _));
        Assert.True(body.TryGetProperty("timestamp", out _));
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    // ---------- DELETE /business/businesses/{businessId}/schedule-exceptions/{exceptionId} ----------

    [Fact]
    public async Task DeleteException_Owner_Returns204AndRemovesOnlyThatException()
    {
        var (business, ownerId) = Reset();
        var target = SeedException(business.Id, "2026-12-24");
        var kept = SeedException(business.Id, "2026-12-25");
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/schedule-exceptions/{target.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(kept.Id, Assert.Single(factory.ScheduleExceptionRepository.All).Id);
    }

    [Fact]
    public async Task DeleteException_OwnerOfInactiveBusiness_Returns204()
    {
        var (business, ownerId) = Reset();
        business.IsActive = false;
        var target = SeedException(business.Id, "2026-12-25");
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/schedule-exceptions/{target.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task DeleteException_NonOwnerBusinessAccount_Returns403AndKeepsIt()
    {
        var (business, _) = Reset();
        var exception = SeedException(business.Id, "2026-12-25");
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/schedule-exceptions/{exception.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
        Assert.Single(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task DeleteException_ClientRole_Returns403AndKeepsIt()
    {
        var (business, ownerId) = Reset();
        var exception = SeedException(business.Id, "2026-12-25");
        var client = CreateClient(ownerId, "CLIENT");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/schedule-exceptions/{exception.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task DeleteException_NoToken_Returns401()
    {
        var (business, _) = Reset();
        var exception = SeedException(business.Id, "2026-12-25");
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/schedule-exceptions/{exception.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(factory.ScheduleExceptionRepository.All);
    }

    [Fact]
    public async Task DeleteException_UnknownException_Returns404()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/schedule-exceptions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeleteException_UnknownBusiness_Returns404()
    {
        Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{Guid.NewGuid()}/schedule-exceptions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteException_ExceptionOfAnotherBusiness_Returns404AndKeepsIt()
    {
        // The caller owns business A and targets an exception of business B through A's route.
        var (business, ownerId) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        var foreign = SeedException(other.Id, "2026-12-25");
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/schedule-exceptions/{foreign.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(foreign.Id, Assert.Single(factory.ScheduleExceptionRepository.All).Id);
    }

    /// <summary>Clears the fakes and seeds one active business, returning it and its owner's account id.</summary>
    private (BusinessEntity Business, Guid OwnerId) Reset()
    {
        factory.BusinessRepository.Reset();
        factory.ScheduleExceptionRepository.Reset();
        var ownerId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = ownerId, Name = "Bella" });
        return (business, ownerId);
    }

    /// <summary>Seeds a closed-day exception on <paramref name="date"/> (YYYY-MM-DD).</summary>
    private BusinessScheduleException SeedException(Guid businessId, string date) =>
        factory.ScheduleExceptionRepository.Seed(new BusinessScheduleException
        {
            BusinessId = businessId,
            ExceptionDate = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            IsClosed = true,
        });

    private static IEnumerable<string?> Dates(JsonElement body) =>
        body.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("exceptionDate").GetString()).ToList();

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }
}
