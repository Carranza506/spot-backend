using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Spot.Booking.Api.Models;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Controllers;

/// <summary>
/// GET /booking/bookings, GET /booking/bookings/{bookingId} and
/// GET /booking/businesses/{businessId}/bookings (#68), through the real pipeline and BookingService
/// with the in-memory fakes of <see cref="BookingsApiFactory"/>.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class BookingReadsControllerTests(BookingsApiFactory factory) : IClassFixture<BookingsApiFactory>
{
    private readonly Guid _client = Guid.NewGuid();
    private readonly Guid _otherClient = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _otherOwner = Guid.NewGuid();

    // ---------- GET /booking/bookings ----------

    [Fact]
    public async Task ListOwn_ReturnsOnlyTheCallersBookings_MostRecentFirst()
    {
        var businessId = SeedBusiness(_owner);
        var older = AddBooking(_client, businessId, "2026-09-01T14:00:00Z");
        var newer = AddBooking(_client, businessId, "2026-09-03T14:00:00Z");
        AddBooking(_otherClient, businessId, "2026-09-02T14:00:00Z");

        var response = await Client(_client, "CLIENT").GetAsync("/booking/bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([newer.Id, older.Id], Ids(body));
        AssertPagination(body, page: 1, pageSize: 20, total: 2, totalPages: 1);
        var first = body.GetProperty("data")[0];
        Assert.Equal(
            ["id", "userId", "businessId", "serviceId", "startAt", "endAt", "status", "serviceName",
             "servicePrice", "serviceDurationMinutes", "notes", "createdAt", "updatedAt"],
            first.EnumerateObject().Select(p => p.Name));
        Assert.Equal("2026-09-03T14:00:00Z", first.GetProperty("startAt").GetString());
    }

    [Fact]
    public async Task ListOwn_StatusFilter_ReturnsOnlyThatStatus()
    {
        var businessId = SeedBusiness(_owner);
        AddBooking(_client, businessId, "2026-09-01T14:00:00Z", BookingStatus.CANCELLED);
        var confirmed = AddBooking(_client, businessId, "2026-09-02T14:00:00Z", BookingStatus.CONFIRMED);

        var response = await Client(_client, "CLIENT").GetAsync("/booking/bookings?status=CONFIRMED");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([confirmed.Id], Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task ListOwn_Paginates()
    {
        var businessId = SeedBusiness(_owner);
        AddBooking(_client, businessId, "2026-09-01T14:00:00Z");
        var middle = AddBooking(_client, businessId, "2026-09-02T14:00:00Z");
        AddBooking(_client, businessId, "2026-09-03T14:00:00Z");

        var response = await Client(_client, "CLIENT").GetAsync("/booking/bookings?page=2&pageSize=1");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([middle.Id], Ids(body));
        AssertPagination(body, page: 2, pageSize: 1, total: 3, totalPages: 3);
    }

    [Theory]
    [InlineData("BUSINESS")]
    [InlineData("SUPERADMIN")]
    public async Task ListOwn_AnyAuthenticatedRole_Returns200(string role)
    {
        SeedBusiness(_owner);

        var response = await Client(Guid.NewGuid(), role).GetAsync("/booking/bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Theory]
    [InlineData("?status=pending")]
    [InlineData("?status=1")]
    [InlineData("?status=UNKNOWN")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task ListOwn_InvalidQuery_Returns400(string query)
    {
        SeedBusiness(_owner);

        var response = await Client(_client, "CLIENT").GetAsync($"/booking/bookings{query}");

        await AssertError(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
    }

    [Fact]
    public async Task ListOwn_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/booking/bookings");

        await AssertError(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    // ---------- GET /booking/bookings/{bookingId} ----------

    [Fact]
    public async Task Get_TheBookingsClient_Returns200()
    {
        var booking = AddBooking(_client, SeedBusiness(_owner), "2026-09-01T14:00:00Z");

        var response = await Client(_client, "CLIENT").GetAsync($"/booking/bookings/{booking.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(booking.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(_client, body.GetProperty("userId").GetGuid());
        Assert.Equal("PENDING", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_TheBusinessOwner_Returns200()
    {
        var booking = AddBooking(_client, SeedBusiness(_owner), "2026-09-01T14:00:00Z");

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/bookings/{booking.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_SuperAdmin_Returns200()
    {
        var booking = AddBooking(_client, SeedBusiness(_owner), "2026-09-01T14:00:00Z");

        var response = await Client(Guid.NewGuid(), "SUPERADMIN").GetAsync($"/booking/bookings/{booking.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_AnotherClient_Returns403()
    {
        var booking = AddBooking(_client, SeedBusiness(_owner), "2026-09-01T14:00:00Z");

        var response = await Client(_otherClient, "CLIENT").GetAsync($"/booking/bookings/{booking.Id}");

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Get_AnotherBusinessOwner_Returns403()
    {
        var booking = AddBooking(_client, SeedBusiness(_owner), "2026-09-01T14:00:00Z");
        SeedBusiness(_otherOwner, reset: false);

        var response = await Client(_otherOwner, "BUSINESS").GetAsync($"/booking/bookings/{booking.Id}");

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("SUPERADMIN")]
    public async Task Get_UnknownBooking_Returns404(string role)
    {
        SeedBusiness(_owner);

        var response = await Client(_client, role).GetAsync($"/booking/bookings/{Guid.NewGuid()}");

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Get_NoToken_Returns401()
    {
        var booking = AddBooking(_client, SeedBusiness(_owner), "2026-09-01T14:00:00Z");

        var response = await factory.CreateClient().GetAsync($"/booking/bookings/{booking.Id}");

        await AssertError(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    // ---------- GET /booking/businesses/{businessId}/bookings ----------

    [Fact]
    public async Task ListBusiness_Owner_ReturnsTheBusinessBookings_EarliestFirst()
    {
        var businessId = SeedBusiness(_owner);
        var otherBusinessId = SeedBusiness(_otherOwner, reset: false);
        var later = AddBooking(_client, businessId, "2026-09-03T14:00:00Z");
        var earlier = AddBooking(_otherClient, businessId, "2026-09-01T14:00:00Z");
        AddBooking(_client, otherBusinessId, "2026-09-02T14:00:00Z");

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/businesses/{businessId}/bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([earlier.Id, later.Id], Ids(body));
        AssertPagination(body, page: 1, pageSize: 20, total: 2, totalPages: 1);
    }

    [Fact]
    public async Task ListBusiness_StatusFilter_ReturnsOnlyThatStatus()
    {
        var businessId = SeedBusiness(_owner);
        var noShow = AddBooking(_client, businessId, "2026-09-01T14:00:00Z", BookingStatus.NO_SHOW);
        AddBooking(_client, businessId, "2026-09-02T14:00:00Z", BookingStatus.PENDING);

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/businesses/{businessId}/bookings?status=NO_SHOW");

        Assert.Equal([noShow.Id], Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task ListBusiness_DateRange_UsesTheBusinessLocalCalendarDays()
    {
        var businessId = SeedBusiness(_owner);
        // Costa Rica is UTC-6: 2026-09-01T05:59Z is still 2026-08-31 locally, 06:00Z is 2026-09-01
        // 00:00, and 2026-09-02T05:59Z is 2026-09-01 23:59.
        AddBooking(_client, businessId, "2026-09-01T05:59:00Z");
        var startOfDay = AddBooking(_client, businessId, "2026-09-01T06:00:00Z");
        var endOfDay = AddBooking(_client, businessId, "2026-09-02T05:59:00Z");
        AddBooking(_client, businessId, "2026-09-02T06:00:00Z");

        var response = await Client(_owner, "BUSINESS")
            .GetAsync($"/booking/businesses/{businessId}/bookings?from=2026-09-01&to=2026-09-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([startOfDay.Id, endOfDay.Id], Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task ListBusiness_OnlyFrom_IsUnboundedAfter()
    {
        var businessId = SeedBusiness(_owner);
        AddBooking(_client, businessId, "2026-08-30T14:00:00Z");
        var after = AddBooking(_client, businessId, "2026-12-01T14:00:00Z");

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/businesses/{businessId}/bookings?from=2026-09-01");

        Assert.Equal([after.Id], Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task ListBusiness_ToIsTheLastRepresentableDate_MeansNoUpperBound()
    {
        var businessId = SeedBusiness(_owner);
        var booking = AddBooking(_client, businessId, "2026-09-01T14:00:00Z");

        var response = await Client(_owner, "BUSINESS")
            .GetAsync($"/booking/businesses/{businessId}/bookings?from=2026-09-01&to=9999-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([booking.Id], Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Theory]
    [InlineData("?from=9999-12-31&to=9999-12-31")]
    [InlineData("?from=0001-01-01&to=0001-01-01")]
    public async Task ListBusiness_ExtremeDates_Return200(string query)
    {
        var businessId = SeedBusiness(_owner);
        AddBooking(_client, businessId, "2026-09-01T14:00:00Z");

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/businesses/{businessId}/bookings{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task ListBusiness_InactiveBusiness_StillReturnsItsBookingsToTheOwner()
    {
        var businessId = SeedBusiness(_owner, isActive: false);
        var booking = AddBooking(_client, businessId, "2026-09-01T14:00:00Z");

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/businesses/{businessId}/bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([booking.Id], Ids(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task ListBusiness_AnotherBusinessOwner_Returns403()
    {
        var businessId = SeedBusiness(_owner);

        var response = await Client(_otherOwner, "BUSINESS").GetAsync($"/booking/businesses/{businessId}/bookings");

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("SUPERADMIN")]
    public async Task ListBusiness_NonBusinessRole_Returns403(string role)
    {
        var businessId = SeedBusiness(_owner);

        var response = await Client(_owner, role).GetAsync($"/booking/businesses/{businessId}/bookings");

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task ListBusiness_UnknownBusiness_Returns404()
    {
        SeedBusiness(_owner);

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/businesses/{Guid.NewGuid()}/bookings");

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("?from=01-09-2026")]
    [InlineData("?to=2026-02-30")]
    [InlineData("?from=2026-09-02&to=2026-09-01")]
    [InlineData("?status=Pending")]
    [InlineData("?pageSize=101")]
    public async Task ListBusiness_InvalidQuery_Returns400(string query)
    {
        var businessId = SeedBusiness(_owner);

        var response = await Client(_owner, "BUSINESS").GetAsync($"/booking/businesses/{businessId}/bookings{query}");

        await AssertError(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
    }

    [Fact]
    public async Task ListBusiness_NoToken_Returns401()
    {
        var businessId = SeedBusiness(_owner);

        var response = await factory.CreateClient().GetAsync($"/booking/businesses/{businessId}/bookings");

        await AssertError(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    // ---------- helpers ----------

    /// <summary>Adds a business owned by <paramref name="accountId"/>, by default after resetting the fakes.</summary>
    private Guid SeedBusiness(Guid accountId, bool isActive = true, bool reset = true)
    {
        if (reset)
        {
            factory.ScheduleRepository.Reset();
            factory.BookingRepository.Reset();
        }

        var businessId = Guid.NewGuid();
        factory.ScheduleRepository.Businesses.Add(new BusinessReference { Id = businessId, AccountId = accountId, IsActive = isActive });
        return businessId;
    }

    private BookingEntity AddBooking(Guid userId, Guid businessId, string startUtc, BookingStatus status = BookingStatus.PENDING)
    {
        var start = DateTimeOffset.Parse(startUtc, CultureInfo.InvariantCulture);
        var booking = new BookingEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BusinessId = businessId,
            ServiceId = Guid.NewGuid(),
            StartAt = start,
            EndAt = start.AddMinutes(45),
            Status = status,
            ServiceName = "Corte de cabello",
            ServicePrice = 12000m,
            ServiceDurationMinutes = 45,
            CreatedAt = BookingsApiFactory.Now,
            UpdatedAt = BookingsApiFactory.Now,
        };
        factory.BookingRepository.Created.Add(booking);
        return booking;
    }

    private static List<Guid> Ids(JsonElement page) =>
        page.GetProperty("data").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();

    private static void AssertPagination(JsonElement body, int page, int pageSize, int total, int totalPages)
    {
        var pagination = body.GetProperty("pagination");
        Assert.Equal(page, pagination.GetProperty("page").GetInt32());
        Assert.Equal(pageSize, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(total, pagination.GetProperty("total").GetInt32());
        Assert.Equal(totalPages, pagination.GetProperty("totalPages").GetInt32());
    }

    private HttpClient Client(Guid userId, string role)
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
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.True(body.TryGetProperty("timestamp", out _));
    }
}
