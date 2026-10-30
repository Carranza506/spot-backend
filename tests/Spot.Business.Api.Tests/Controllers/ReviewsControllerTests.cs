using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Controllers;

/// <summary>
/// POST /booking/bookings/{bookingId}/review and GET /booking/businesses/{businessId}/reviews (#70),
/// served by Business.Api. Same factory as FavoritesControllerTests: the real pipeline and
/// ReviewService, repositories faked (the fake emulates trg_validate_review and the unique booking_id).
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class ReviewsControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // ---------- POST /booking/bookings/{bookingId}/review ----------

    [Fact]
    public async Task Create_TheBookingsClient_Returns201WithTheReview()
    {
        Reset();
        var clientId = Guid.NewGuid();
        var business = SeedBusiness("Bella");
        var booking = factory.ReviewRepository.SeedBooking(clientId, business.Id);

        var response = await CreateClient(clientId, "CLIENT")
            .PostAsJsonAsync($"/booking/bookings/{booking.Id}/review", new { rating = 5, comment = "  Excelente.  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(booking.Id, body.GetProperty("bookingId").GetGuid());
        Assert.Equal(clientId, body.GetProperty("userId").GetGuid());
        Assert.Equal(business.Id, body.GetProperty("businessId").GetGuid());
        Assert.Equal(5, body.GetProperty("rating").GetInt32());
        Assert.Equal("Excelente.", body.GetProperty("comment").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.True(body.TryGetProperty("createdAt", out _));
        Assert.True(body.TryGetProperty("updatedAt", out _));
        Assert.Single(factory.ReviewRepository.All);
    }

    [Fact]
    public async Task Create_WithoutAComment_StoresNull()
    {
        Reset();
        var clientId = Guid.NewGuid();
        var booking = factory.ReviewRepository.SeedBooking(clientId, SeedBusiness("Bella").Id);

        var response = await CreateClient(clientId, "CLIENT")
            .PostAsJsonAsync($"/booking/bookings/{booking.Id}/review", new { rating = 4, comment = "   " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(Assert.Single(factory.ReviewRepository.All).Comment);
    }

    [Fact]
    public async Task Create_ABookingThatIsNotCompleted_Returns422AndCreatesNothing()
    {
        Reset();
        var clientId = Guid.NewGuid();
        var booking = factory.ReviewRepository.SeedBooking(clientId, SeedBusiness("Bella").Id, completed: false);

        var response = await CreateClient(clientId, "CLIENT")
            .PostAsJsonAsync($"/booking/bookings/{booking.Id}/review", new { rating = 5 });

        await AssertError(response, HttpStatusCode.UnprocessableEntity, "REVIEW_BOOKING_NOT_COMPLETED");
        Assert.Empty(factory.ReviewRepository.All);
    }

    [Fact]
    public async Task Create_ASecondReviewOfTheSameBooking_Returns409()
    {
        Reset();
        var clientId = Guid.NewGuid();
        var booking = factory.ReviewRepository.SeedBooking(clientId, SeedBusiness("Bella").Id);
        var client = CreateClient(clientId, "CLIENT");
        (await client.PostAsJsonAsync($"/booking/bookings/{booking.Id}/review", new { rating = 5 })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/booking/bookings/{booking.Id}/review", new { rating = 1 });

        await AssertError(response, HttpStatusCode.Conflict, "REVIEW_ALREADY_EXISTS");
        Assert.Equal(5, Assert.Single(factory.ReviewRepository.All).Rating);
    }

    [Fact]
    public async Task Create_SomeoneElsesBooking_Returns403BeforeCheckingItsStatus()
    {
        Reset();
        // Not completed, so its client would get a 422: a stranger must get 403 instead.
        var booking = factory.ReviewRepository.SeedBooking(Guid.NewGuid(), SeedBusiness("Bella").Id, completed: false);

        var response = await CreateClient(Guid.NewGuid(), "CLIENT")
            .PostAsJsonAsync($"/booking/bookings/{booking.Id}/review", new { rating = 5 });

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(factory.ReviewRepository.All);
    }

    [Fact]
    public async Task Create_AnUnknownBooking_Returns404()
    {
        Reset();

        var response = await CreateClient(Guid.NewGuid(), "CLIENT")
            .PostAsJsonAsync($"/booking/bookings/{Guid.NewGuid()}/review", new { rating = 5 });

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("BUSINESS")]
    [InlineData("SUPERADMIN")]
    public async Task Create_ANonClientRole_Returns403(string role)
    {
        Reset();
        // The caller even is the booking's user: the role alone rules it out.
        var callerId = Guid.NewGuid();
        var booking = factory.ReviewRepository.SeedBooking(callerId, SeedBusiness("Bella").Id);

        var response = await CreateClient(callerId, role)
            .PostAsJsonAsync($"/booking/bookings/{booking.Id}/review", new { rating = 5 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.ReviewRepository.All);
    }

    [Fact]
    public async Task Create_WithoutAToken_Returns401()
    {
        Reset();

        var response = await factory.CreateClient()
            .PostAsJsonAsync($"/booking/bookings/{Guid.NewGuid()}/review", new { rating = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"rating":0}""")]
    [InlineData("""{"rating":6}""")]
    [InlineData("""{"comment":"Sin rating"}""")]
    [InlineData("""{"rating":"cinco"}""")]
    public async Task Create_AnInvalidBody_Returns400AndCreatesNothing(string json)
    {
        Reset();
        var clientId = Guid.NewGuid();
        var booking = factory.ReviewRepository.SeedBooking(clientId, SeedBusiness("Bella").Id);

        var response = await CreateClient(clientId, "CLIENT").PostAsync(
            $"/booking/bookings/{booking.Id}/review", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.ReviewRepository.All);
    }

    // ---------- GET /booking/businesses/{businessId}/reviews ----------

    [Fact]
    public async Task List_IsPublicAndReturnsTheBusinessesReviewsNewestFirst()
    {
        Reset();
        var business = SeedBusiness("Bella");
        var older = factory.ReviewRepository.SeedReview(business.Id, 3, T0, "Bien.");
        var newer = factory.ReviewRepository.SeedReview(business.Id, 5, T0.AddDays(1));
        factory.ReviewRepository.SeedReview(SeedBusiness("Otro").Id, 1, T0);

        var response = await factory.CreateClient().GetAsync($"/booking/businesses/{business.Id}/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal([newer.Id, older.Id], data.Select(r => r.GetProperty("id").GetGuid()));
        Assert.Equal("Bien.", data[1].GetProperty("comment").GetString());
        Assert.Equal(JsonValueKind.Null, data[0].GetProperty("comment").ValueKind);
        Assert.Equal(2, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task List_Pagination_ReturnsTheRequestedPage()
    {
        Reset();
        var business = SeedBusiness("Bella");
        var reviews = Enumerable.Range(0, 5).Select(i => factory.ReviewRepository.SeedReview(business.Id, 4, T0.AddDays(i))).ToList();

        var response = await factory.CreateClient().GetAsync($"/booking/businesses/{business.Id}/reviews?page=2&pageSize=2");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([reviews[2].Id, reviews[1].Id], body.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
        var pagination = body.GetProperty("pagination");
        Assert.Equal(5, pagination.GetProperty("total").GetInt32());
        Assert.Equal(3, pagination.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task List_AnUnknownOrInactiveBusiness_Returns404()
    {
        Reset();
        var inactive = SeedBusiness("Cerrado", isActive: false);
        factory.ReviewRepository.SeedReview(inactive.Id, 5, T0);

        var client = factory.CreateClient();
        await AssertError(await client.GetAsync($"/booking/businesses/{Guid.NewGuid()}/reviews"), HttpStatusCode.NotFound, "NOT_FOUND");
        await AssertError(await client.GetAsync($"/booking/businesses/{inactive.Id}/reviews"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_InvalidPaging_Returns400(string query)
    {
        Reset();
        var business = SeedBusiness("Bella");

        var response = await factory.CreateClient().GetAsync($"/booking/businesses/{business.Id}/reviews?{query}");

        await AssertError(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
    }

    private void Reset()
    {
        factory.BusinessRepository.Reset();
        factory.ReviewRepository.Reset();
    }

    private BusinessEntity SeedBusiness(string name, bool isActive = true) =>
        factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = name, IsActive = isActive });

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }
}
