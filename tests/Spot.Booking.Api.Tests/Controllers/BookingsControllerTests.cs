using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.Tests.Controllers;

[Collection(ApiFactoryCollection.Name)]
public class BookingsControllerTests(BookingsApiFactory factory) : IClassFixture<BookingsApiFactory>
{
    // 2026-09-01 is a Tuesday. The seeded business opens that weekday 08:00–10:00 local
    // (14:00Z–16:00Z); its service lasts 45 minutes. The clock is 2026-08-31T12:00Z.
    private static readonly DateOnly Day = new(2026, 9, 1);

    // ---------- 201 ----------

    [Fact]
    public async Task Create_Client_Returns201WithTheBooking()
    {
        var (businessId, serviceId) = Seed();
        var userId = Guid.NewGuid();

        var response = await Client(userId, "CLIENT").PostAsJsonAsync("/booking/bookings", new
        {
            businessId,
            serviceId,
            startAt = "2026-09-01T14:00:00Z",
            notes = "Preferiblemente con la estilista Ana.",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["id", "userId", "businessId", "serviceId", "startAt", "endAt", "status", "serviceName",
             "servicePrice", "serviceDurationMinutes", "notes", "createdAt", "updatedAt"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(userId, body.GetProperty("userId").GetGuid());
        Assert.Equal(businessId, body.GetProperty("businessId").GetGuid());
        Assert.Equal(serviceId, body.GetProperty("serviceId").GetGuid());
        Assert.Equal("2026-09-01T14:00:00Z", body.GetProperty("startAt").GetString());
        Assert.Equal("2026-09-01T14:45:00Z", body.GetProperty("endAt").GetString());
        Assert.Equal("PENDING", body.GetProperty("status").GetString());
        Assert.Equal("Corte de cabello", body.GetProperty("serviceName").GetString());
        Assert.Equal(12000.50m, body.GetProperty("servicePrice").GetDecimal());
        Assert.Equal(45, body.GetProperty("serviceDurationMinutes").GetInt32());
        Assert.Equal("Preferiblemente con la estilista Ana.", body.GetProperty("notes").GetString());
        Assert.EndsWith("Z", body.GetProperty("createdAt").GetString());
        Assert.EndsWith("Z", body.GetProperty("updatedAt").GetString());

        var stored = Assert.Single(factory.BookingRepository.Created);
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(BookingStatus.PENDING, stored.Status);
        Assert.Equal(TimeSpan.Zero, stored.StartAt.Offset);
    }

    [Fact]
    public async Task Create_StartWithOffsetOffTheSlotGrid_IsConvertedToUtc()
    {
        var (businessId, serviceId) = Seed();

        var response = await Post(Body(businessId, serviceId, "2026-09-01T08:10:00-06:00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-09-01T14:10:00Z", body.GetProperty("startAt").GetString());
        Assert.Equal("2026-09-01T14:55:00Z", body.GetProperty("endAt").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("notes").ValueKind);
    }

    [Fact]
    public async Task Create_EndingExactlyAtClosing_Returns201()
    {
        var (businessId, serviceId) = Seed();

        var response = await Post(Body(businessId, serviceId, "2026-09-01T15:15:00Z"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_TheBookedSlotDisappearsFromAvailability()
    {
        var (businessId, serviceId) = Seed();
        Assert.Equal(HttpStatusCode.Created, (await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"))).StatusCode);

        var response = await factory.CreateClient().GetAsync(
            $"/booking/businesses/{businessId}/availability?from=2026-09-01&to=2026-09-01&serviceId={serviceId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var starts = body.GetProperty("days")[0].GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("startAt").GetString());
        Assert.Equal(["2026-09-01T14:45:00Z"], starts);
    }

    // ---------- 401 / 403 ----------

    [Fact]
    public async Task Create_NoToken_Returns401InTheErrorShape()
    {
        var (businessId, serviceId) = Seed();

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/booking/bookings", Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        Assert.Empty(factory.BookingRepository.Created);
    }

    [Fact]
    public async Task Create_InvalidToken_Returns401InTheErrorShape()
    {
        var (businessId, serviceId) = Seed();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.PostAsJsonAsync("/booking/bookings", Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData("BUSINESS")]
    [InlineData("SUPERADMIN")]
    public async Task Create_NonClientRole_Returns403InTheErrorShape(string role)
    {
        var (businessId, serviceId) = Seed();

        var response = await Client(Guid.NewGuid(), role).PostAsJsonAsync(
            "/booking/bookings", Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(factory.BookingRepository.Created);
    }

    // ---------- 400 ----------

    public static TheoryData<string> InvalidBodies => new()
    {
        "{}",
        $$"""{ "serviceId": "{{Guid.NewGuid()}}", "startAt": "2026-09-01T14:00:00Z" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "startAt": "2026-09-01T14:00:00Z" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": "{{Guid.NewGuid()}}" }""",
        $$"""{ "businessId": null, "serviceId": "{{Guid.NewGuid()}}", "startAt": "2026-09-01T14:00:00Z" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": null, "startAt": "2026-09-01T14:00:00Z" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": "{{Guid.NewGuid()}}", "startAt": null }""",
        $$"""{ "businessId": "not-a-guid", "serviceId": "{{Guid.NewGuid()}}", "startAt": "2026-09-01T14:00:00Z" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": 12, "startAt": "2026-09-01T14:00:00Z" }""",
        // No offset: would otherwise be read in the server's time zone.
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": "{{Guid.NewGuid()}}", "startAt": "2026-09-01T14:00:00" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": "{{Guid.NewGuid()}}", "startAt": "2026-09-01" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": "{{Guid.NewGuid()}}", "startAt": "mañana" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": "{{Guid.NewGuid()}}", "startAt": "2026-02-30T14:00:00Z" }""",
        $$"""{ "businessId": "{{Guid.NewGuid()}}", "serviceId": "{{Guid.NewGuid()}}", "startAt": 1788271200 }""",
        """{ "businessId": """,
        "[]",
        "null",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_InvalidBody_Returns400InTheErrorShape(string json)
    {
        Seed();

        var response = await Client(Guid.NewGuid(), "CLIENT").PostAsync(
            "/booking/bookings", new StringContent(json, Encoding.UTF8, "application/json"));

        await AssertError(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
        Assert.Empty(factory.BookingRepository.Created);
    }

    // ---------- 404 ----------

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    public async Task Create_BusinessNotBookable_Returns404(string scenario)
    {
        var (businessId, serviceId) = Seed();
        if (scenario == "missing")
            factory.ScheduleRepository.Businesses.Clear();
        else
            factory.ScheduleRepository.Businesses.Single().IsActive = false;

        var response = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "El negocio indicado no existe.");
        Assert.Empty(factory.BookingRepository.Created);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("other business")]
    public async Task Create_ServiceNotBookable_Returns404(string scenario)
    {
        var (businessId, serviceId) = Seed();
        var service = factory.ScheduleRepository.Services.Single();
        switch (scenario)
        {
            case "missing":
                factory.ScheduleRepository.Services.Clear();
                break;
            case "inactive":
                service.IsActive = false;
                break;
            default:
                service.BusinessId = Guid.NewGuid();
                break;
        }

        var response = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "El servicio indicado no existe.");
        Assert.Empty(factory.BookingRepository.Created);
    }

    // ---------- 422 ----------

    [Theory]
    [InlineData("closed day (no hours row)", "2026-09-02T14:00:00Z")]
    [InlineData("starts before opening", "2026-09-01T13:59:00Z")]
    [InlineData("ends after closing", "2026-09-01T15:30:00Z")]
    [InlineData("starts after closing", "2026-09-01T16:00:00Z")]
    [InlineData("previous local day (UTC date is the open day)", "2026-09-01T05:00:00Z")]
    public async Task Create_OutsideOpeningHours_Returns422(string _, string startAt)
    {
        var (businessId, serviceId) = Seed();

        var response = await Post(Body(businessId, serviceId, startAt));

        await AssertError(response, HttpStatusCode.UnprocessableEntity, "BOOKING_OUTSIDE_BUSINESS_HOURS");
        Assert.Empty(factory.BookingRepository.Created);
    }

    [Fact]
    public async Task Create_UsesTheLocalDateOfStartAt_NotItsUtcDate()
    {
        var (businessId, serviceId) = Seed();
        // Monday evening hours. 2026-08-31 20:00 local is already Tuesday 02:00 in UTC, when the
        // business (open Tuesdays 08:00–10:00 local) would be closed.
        factory.ScheduleRepository.Hours.Add(new BusinessHoursReference
        {
            Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = (short)DayOfWeek.Monday,
            OpenTime = new TimeOnly(18, 0), CloseTime = new TimeOnly(23, 0),
        });

        var response = await Post(Body(businessId, serviceId, "2026-09-01T02:00:00Z"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WeeklyRowMarkedClosed_Returns422()
    {
        var (businessId, serviceId) = Seed();
        factory.ScheduleRepository.Hours.Single().IsClosed = true;

        var response = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.UnprocessableEntity, "BOOKING_OUTSIDE_BUSINESS_HOURS");
    }

    [Fact]
    public async Task Create_ExceptionClosingTheDay_Returns422()
    {
        var (businessId, serviceId) = Seed();
        AddException(businessId, isClosed: true);

        var response = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.UnprocessableEntity, "BOOKING_OUTSIDE_BUSINESS_HOURS");
    }

    [Fact]
    public async Task Create_ExceptionWithShorterHours_Returns422WhenItDoesNotFit()
    {
        var (businessId, serviceId) = Seed();
        AddException(businessId, isClosed: false, new TimeOnly(8, 0), new TimeOnly(8, 30));

        var response = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.UnprocessableEntity, "BOOKING_OUTSIDE_BUSINESS_HOURS");
    }

    [Fact]
    public async Task Create_ExceptionOpeningAClosedDay_Returns201()
    {
        var (businessId, serviceId) = Seed();
        var wednesday = Day.AddDays(1);
        factory.ScheduleRepository.Exceptions.Add(new BusinessScheduleExceptionReference
        {
            Id = Guid.NewGuid(), BusinessId = businessId, ExceptionDate = wednesday,
            OpenTime = new TimeOnly(9, 0), CloseTime = new TimeOnly(10, 0),
        });

        var response = await Post(Body(businessId, serviceId, "2026-09-02T15:00:00Z"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_StartInThePast_Returns422()
    {
        var (businessId, serviceId) = Seed();

        // 2026-08-25 is the previous Tuesday, within opening hours, but before the fixed clock.
        var response = await Post(Body(businessId, serviceId, "2026-08-25T14:00:00Z"));

        await AssertError(response, HttpStatusCode.UnprocessableEntity, "BOOKING_START_IN_PAST");
        Assert.Empty(factory.BookingRepository.Created);
    }

    // ---------- 409 ----------

    [Theory]
    [InlineData(BookingStatus.PENDING, "2026-09-01T14:30:00Z", "2026-09-01T15:00:00Z")]
    [InlineData(BookingStatus.CONFIRMED, "2026-09-01T13:30:00Z", "2026-09-01T14:15:00Z")]
    public async Task Create_OverlappingAnActiveBooking_Returns409(BookingStatus status, string existingStart, string existingEnd)
    {
        var (businessId, serviceId) = Seed();
        AddExistingBooking(businessId, existingStart, existingEnd, status);

        var response = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        await AssertError(response, HttpStatusCode.Conflict, "BOOKING_SLOT_OVERLAP");
        Assert.Empty(factory.BookingRepository.Created);
    }

    [Fact]
    public async Task Create_SameSlotTwice_SecondReturns409()
    {
        var (businessId, serviceId) = Seed();

        var first = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));
        var second = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertError(second, HttpStatusCode.Conflict, "BOOKING_SLOT_OVERLAP");
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED, "2026-09-01T14:00:00Z", "2026-09-01T14:45:00Z")]
    [InlineData(BookingStatus.CONFIRMED, "2026-09-01T13:15:00Z", "2026-09-01T14:00:00Z")] // back-to-back
    public async Task Create_NotOverlappingAnActiveBooking_Returns201(BookingStatus status, string existingStart, string existingEnd)
    {
        var (businessId, serviceId) = Seed();
        AddExistingBooking(businessId, existingStart, existingEnd, status);

        var response = await Post(Body(businessId, serviceId, "2026-09-01T14:00:00Z"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---------- helpers ----------

    /// <summary>Resets the fakes, then seeds an active business open Tuesdays 08:00–10:00 local with a 45-minute service.</summary>
    private (Guid BusinessId, Guid ServiceId) Seed()
    {
        factory.ScheduleRepository.Reset();
        factory.BookingRepository.Reset();

        var businessId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        factory.ScheduleRepository.Businesses.Add(new BusinessReference { Id = businessId, IsActive = true });
        factory.ScheduleRepository.Services.Add(new ServiceReference
        {
            Id = serviceId, BusinessId = businessId, Name = "Corte de cabello", Price = 12000.50m,
            DurationMinutes = 45, IsActive = true,
        });
        factory.ScheduleRepository.Hours.Add(new BusinessHoursReference
        {
            Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = (short)Day.DayOfWeek,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(10, 0),
        });
        return (businessId, serviceId);
    }

    private void AddException(Guid businessId, bool isClosed, TimeOnly? open = null, TimeOnly? close = null) =>
        factory.ScheduleRepository.Exceptions.Add(new BusinessScheduleExceptionReference
        {
            Id = Guid.NewGuid(), BusinessId = businessId, ExceptionDate = Day,
            IsClosed = isClosed, OpenTime = open, CloseTime = close,
        });

    private void AddExistingBooking(Guid businessId, string startUtc, string endUtc, BookingStatus status) =>
        factory.ScheduleRepository.Bookings.Add((businessId, new BookedInterval(
            DateTimeOffset.Parse(startUtc, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(endUtc, System.Globalization.CultureInfo.InvariantCulture),
            status)));

    private static object Body(Guid businessId, Guid serviceId, string startAt) => new { businessId, serviceId, startAt };

    private Task<HttpResponseMessage> Post(object body) =>
        Client(Guid.NewGuid(), "CLIENT").PostAsJsonAsync("/booking/bookings", body);

    private HttpClient Client(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code, string? message = null)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.True(body.TryGetProperty("timestamp", out _));
        if (message is not null)
            Assert.Equal(message, body.GetProperty("message").GetString());
    }
}
