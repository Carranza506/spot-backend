using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Business.Api.Models;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Controllers;

/// <summary>
/// GET/PUT /business/businesses/{businessId}/hours (#60). Same factory as BusinessesControllerTests:
/// the real pipeline and the real BusinessService, with the repositories faked.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class BusinessHoursControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    // ---------- GET /business/businesses/{businessId}/hours ----------

    [Fact]
    public async Task ListBusinessHours_NoToken_Returns200OrderedByDayOfWeek()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        factory.BusinessRepository.SeedHours(business.Id,
            OpenDay(3, 9, 18), new BusinessHours { DayOfWeek = 0, IsClosed = true }, OpenDay(1, 8, 17));
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/hours");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal([0, 1, 3], data.Select(d => d.GetProperty("dayOfWeek").GetInt32()));
        Assert.True(data[0].GetProperty("isClosed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data[0].GetProperty("openTime").ValueKind);
        Assert.Equal("08:00:00", data[1].GetProperty("openTime").GetString());
        Assert.Equal("17:00:00", data[1].GetProperty("closeTime").GetString());
        Assert.Equal(3, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListBusinessHours_Pagination_ReturnsTheRequestedPage()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        factory.BusinessRepository.SeedHours(business.Id, Enumerable.Range(0, 7).Select(d => OpenDay(d, 9, 18)).ToArray());
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/hours?page=2&pageSize=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([3, 4, 5], body.GetProperty("data").EnumerateArray().Select(d => d.GetProperty("dayOfWeek").GetInt32()));
        var pagination = body.GetProperty("pagination");
        Assert.Equal(2, pagination.GetProperty("page").GetInt32());
        Assert.Equal(3, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(7, pagination.GetProperty("total").GetInt32());
        Assert.Equal(3, pagination.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task ListBusinessHours_InvalidPagination_Returns400(string queryString)
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/hours?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListBusinessHours_UnknownBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{Guid.NewGuid()}/hours");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBusinessHours_InactiveBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(
            new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", IsActive = false });
        factory.BusinessRepository.SeedHours(business.Id, OpenDay(1, 9, 18));
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/hours");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /business/businesses/{businessId}/hours ----------

    [Fact]
    public async Task ReplaceBusinessHours_OwnerFullWeek_Returns200WithEveryDayOrdered()
    {
        var (business, client) = SeedOwnedBusiness();
        var week = new[] { 6, 3, 0, 5, 1, 4, 2 }.Select(d => d == 0
            ? (object)new { dayOfWeek = d, isClosed = true }
            : new { dayOfWeek = d, isClosed = false, openTime = "09:00:00", closeTime = "18:00:00" });

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", new { hours = week });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], data.Select(d => d.GetProperty("dayOfWeek").GetInt32()));
        Assert.All(data, d => Assert.Equal(business.Id, d.GetProperty("businessId").GetGuid()));
        Assert.Equal("09:00:00", data[1].GetProperty("openTime").GetString());
        Assert.Equal("18:00:00", data[1].GetProperty("closeTime").GetString());
        Assert.False(body.TryGetProperty("pagination", out _));
        Assert.Equal(7, factory.BusinessRepository.HoursOf(business.Id).Count);
    }

    [Fact]
    public async Task ReplaceBusinessHours_PartialWeek_DeletesTheDaysNotSent()
    {
        var (business, client) = SeedOwnedBusiness();
        factory.BusinessRepository.SeedHours(business.Id, Enumerable.Range(0, 7).Select(d => OpenDay(d, 9, 18)).ToArray());
        var mondayId = factory.BusinessRepository.HoursOf(business.Id).Single(h => h.DayOfWeek == 1).Id;

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", new
        {
            hours = new object[]
            {
                new { dayOfWeek = 2, isClosed = false, openTime = "10:00:00", closeTime = "14:00:00" },
                new { dayOfWeek = 1, isClosed = false, openTime = "08:30:00", closeTime = "12:00:00" },
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([1, 2], body.GetProperty("data").EnumerateArray().Select(d => d.GetProperty("dayOfWeek").GetInt32()));
        var stored = factory.BusinessRepository.HoursOf(business.Id);
        Assert.Equal([1, 2], stored.Select(h => (int)h.DayOfWeek));
        Assert.Equal(mondayId, stored[0].Id);
        Assert.Equal(new TimeOnly(8, 30), stored[0].OpenTime);
    }

    [Fact]
    public async Task ReplaceBusinessHours_ClosedDayWithTimes_StoresNullTimes()
    {
        var (business, client) = SeedOwnedBusiness();

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", new
        {
            hours = new[] { new { dayOfWeek = 0, isClosed = true, openTime = "09:00:00", closeTime = "18:00:00" } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var day = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")[0];
        Assert.Equal(JsonValueKind.Null, day.GetProperty("openTime").ValueKind);
        Assert.Equal(JsonValueKind.Null, day.GetProperty("closeTime").ValueKind);
        var stored = factory.BusinessRepository.HoursOf(business.Id).Single();
        Assert.True(stored.IsClosed);
        Assert.Null(stored.OpenTime);
        Assert.Null(stored.CloseTime);
    }

    [Fact]
    public async Task ReplaceBusinessHours_ClosedDayWithExplicitNullTimes_Returns200()
    {
        var (business, client) = SeedOwnedBusiness();

        var response = await PutRaw(client, business.Id,
            "{\"hours\":[{\"dayOfWeek\":0,\"isClosed\":true,\"openTime\":null,\"closeTime\":null}]}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.BusinessRepository.HoursOf(business.Id).Single().IsClosed);
    }

    [Fact]
    public async Task ReplaceBusinessHours_OwnerOfInactiveBusiness_Returns200()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(factory.BusinessRepository.HoursOf(business.Id));
    }

    [Fact]
    public async Task ReplaceBusinessHours_NonOwnerBusinessAccount_Returns403AndKeepsTheSchedule()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        factory.BusinessRepository.SeedHours(business.Id, OpenDay(3, 9, 18));
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
        Assert.Equal(3, factory.BusinessRepository.HoursOf(business.Id).Single().DayOfWeek);
    }

    [Fact]
    public async Task ReplaceBusinessHours_ClientRole_Returns403()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var client = CreateClient(accountId, "CLIENT");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceBusinessHours_NoToken_Returns401()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceBusinessHours_UnknownBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{Guid.NewGuid()}/hours", ValidBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"hours\":[]}")]
    [InlineData("{}")]
    [InlineData("{\"hours\":[null]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"openTime\":\"09:00:00\",\"closeTime\":\"18:00:00\"}]}")]
    [InlineData("{\"hours\":[{\"isClosed\":true}]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":7,\"isClosed\":true}]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":-1,\"isClosed\":true}]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00\",\"closeTime\":\"18:00:00\"}]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"9:00:00\",\"closeTime\":\"18:00:00\"}]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00:00.5\",\"closeTime\":\"18:00:00\"}]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":\"24:00:00\"}]}")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":\"6pm\"}]}")]
    public async Task ReplaceBusinessHours_InvalidBody_Returns400AndKeepsTheSchedule(string json)
    {
        var (business, client) = SeedOwnedBusiness();
        factory.BusinessRepository.SeedHours(business.Id, OpenDay(3, 9, 18));

        var response = await PutRaw(client, business.Id, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Equal(3, factory.BusinessRepository.HoursOf(business.Id).Single().DayOfWeek);
    }

    [Fact]
    public async Task ReplaceBusinessHours_MoreThanSevenDays_Returns400()
    {
        var (business, client) = SeedOwnedBusiness();
        var eight = Enumerable.Range(0, 8).Select(d => new { dayOfWeek = d % 7, isClosed = true });

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/hours", new { hours = eight });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(
        "{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":true},{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":\"18:00:00\"}]}",
        "DUPLICATE_DAY_OF_WEEK")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false}]}", "MISSING_OPENING_HOURS")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00:00\"}]}", "MISSING_OPENING_HOURS")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":null}]}", "MISSING_OPENING_HOURS")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"18:00:00\",\"closeTime\":\"09:00:00\"}]}", "INVALID_TIME_RANGE")]
    [InlineData("{\"hours\":[{\"dayOfWeek\":1,\"isClosed\":false,\"openTime\":\"09:00:00\",\"closeTime\":\"09:00:00\"}]}", "INVALID_TIME_RANGE")]
    public async Task ReplaceBusinessHours_BrokenScheduleRule_Returns422WithCodeAndKeepsTheSchedule(string json, string expectedCode)
    {
        var (business, client) = SeedOwnedBusiness();
        factory.BusinessRepository.SeedHours(business.Id, OpenDay(3, 9, 18));

        var response = await PutRaw(client, business.Id, json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
        Assert.True(body.TryGetProperty("timestamp", out _));
        Assert.Equal(3, factory.BusinessRepository.HoursOf(business.Id).Single().DayOfWeek);
    }

    private static BusinessHours OpenDay(int dayOfWeek, int openHour, int closeHour) => new()
    {
        DayOfWeek = (short)dayOfWeek,
        OpenTime = new TimeOnly(openHour, 0),
        CloseTime = new TimeOnly(closeHour, 0),
        IsClosed = false,
    };

    private static object ValidBody() => new
    {
        hours = new[] { new { dayOfWeek = 1, isClosed = false, openTime = "09:00:00", closeTime = "18:00:00" } },
    };

    private (BusinessEntity Business, HttpClient Client) SeedOwnedBusiness()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        return (business, CreateClient(accountId, "BUSINESS"));
    }

    private static Task<HttpResponseMessage> PutRaw(HttpClient client, Guid businessId, string json) =>
        client.PutAsync($"/business/businesses/{businessId}/hours", new StringContent(json, Encoding.UTF8, "application/json"));

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }
}
