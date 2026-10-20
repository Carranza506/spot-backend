using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.Tests.Controllers;

public class AvailabilityControllerTests(AvailabilityApiFactory factory) : IClassFixture<AvailabilityApiFactory>
{
    // 2026-09-01 is a Tuesday. The seeded business opens that weekday 08:00–10:00 local (14:00Z–16:00Z).
    private static readonly DateOnly Day = new(2026, 9, 1);

    // ---------- 200 ----------

    [Fact]
    public async Task Get_Returns200WithTheContractShape()
    {
        var businessId = SeedBusiness();

        var response = await factory.CreateClient().GetAsync(Url(businessId, "from=2026-09-01&to=2026-09-02"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["businessId", "serviceId", "from", "to", "days"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(businessId, body.GetProperty("businessId").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("serviceId").ValueKind);
        Assert.Equal("2026-09-01", body.GetProperty("from").GetString());
        Assert.Equal("2026-09-02", body.GetProperty("to").GetString());

        var days = body.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(2, days.Count);

        var open = days[0];
        Assert.Equal(["date", "isClosed", "slots"], open.EnumerateObject().Select(p => p.Name));
        Assert.Equal("2026-09-01", open.GetProperty("date").GetString());
        Assert.False(open.GetProperty("isClosed").GetBoolean());
        var slots = open.GetProperty("slots").EnumerateArray().ToList();
        Assert.Equal(4, slots.Count); // default slotMinutes = 30
        Assert.Equal(["startAt", "endAt"], slots[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal("2026-09-01T14:00:00Z", slots[0].GetProperty("startAt").GetString());
        Assert.Equal("2026-09-01T14:30:00Z", slots[0].GetProperty("endAt").GetString());
        Assert.Equal("2026-09-01T15:30:00Z", slots[3].GetProperty("startAt").GetString());

        var closed = days[1];
        Assert.Equal("2026-09-02", closed.GetProperty("date").GetString());
        Assert.True(closed.GetProperty("isClosed").GetBoolean());
        Assert.Empty(closed.GetProperty("slots").EnumerateArray());
    }

    [Fact]
    public async Task Get_SlotMinutes_SetsTheSlotLength()
    {
        var businessId = SeedBusiness();

        var days = await GetDays(businessId, "from=2026-09-01&to=2026-09-01&slotMinutes=60");

        Assert.Equal(["2026-09-01T14:00:00Z", "2026-09-01T15:00:00Z"], SlotStarts(days[0]));
    }

    [Fact]
    public async Task Get_ServiceId_UsesTheServiceDurationOverSlotMinutes_AndEchoesIt()
    {
        var businessId = SeedBusiness();
        var serviceId = SeedService(businessId, durationMinutes: 45);

        var response = await factory.CreateClient().GetAsync(
            Url(businessId, $"from=2026-09-01&to=2026-09-01&serviceId={serviceId}&slotMinutes=30"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(serviceId, body.GetProperty("serviceId").GetGuid());
        var slots = body.GetProperty("days")[0].GetProperty("slots").EnumerateArray().ToList();
        Assert.Equal(["2026-09-01T14:00:00Z", "2026-09-01T14:45:00Z"], slots.Select(s => s.GetProperty("startAt").GetString()));
        Assert.Equal("2026-09-01T15:30:00Z", slots[1].GetProperty("endAt").GetString());
    }

    [Fact]
    public async Task Get_ActiveBookingsRemoveSlots_OtherStatusesDoNot()
    {
        var businessId = SeedBusiness();
        SeedBooking(businessId, "2026-09-01T14:00:00Z", "2026-09-01T14:30:00Z", BookingStatus.CONFIRMED);
        SeedBooking(businessId, "2026-09-01T15:00:00Z", "2026-09-01T15:30:00Z", BookingStatus.CANCELLED);
        // Another business's booking never blocks this one.
        SeedBooking(Guid.NewGuid(), "2026-09-01T14:30:00Z", "2026-09-01T15:00:00Z", BookingStatus.PENDING);

        var days = await GetDays(businessId, "from=2026-09-01&to=2026-09-01");

        Assert.Equal(
            ["2026-09-01T14:30:00Z", "2026-09-01T15:00:00Z", "2026-09-01T15:30:00Z"],
            SlotStarts(days[0]));
    }

    [Fact]
    public async Task Get_ScheduleExceptionOverridesTheWeeklyHours()
    {
        var businessId = SeedBusiness();
        factory.Repository.Exceptions.Add(new BusinessScheduleExceptionReference
        {
            Id = Guid.NewGuid(), BusinessId = businessId, ExceptionDate = Day, IsClosed = true,
        });

        var days = await GetDays(businessId, "from=2026-09-01&to=2026-09-01");

        Assert.True(days[0].GetProperty("isClosed").GetBoolean());
        Assert.Empty(days[0].GetProperty("slots").EnumerateArray());
    }

    [Fact]
    public async Task Get_31DayRange_IsAccepted()
    {
        var businessId = SeedBusiness();

        var days = await GetDays(businessId, "from=2026-09-01&to=2026-10-01");

        Assert.Equal(31, days.Count);
        Assert.Equal("2026-09-01", days[0].GetProperty("date").GetString());
        Assert.Equal("2026-10-01", days[^1].GetProperty("date").GetString());
    }

    // ---------- 400 ----------

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "", "from" },
        { "to=2026-09-01", "from" },
        { "from=2026-09-01", "to" },
        { "from=&to=2026-09-01", "from" },
        { "from=2026-9-1&to=2026-09-02", "from" },
        { "from=01/09/2026&to=2026-09-02", "from" },
        { "from=2026-09-01T00:00:00&to=2026-09-02", "from" },
        { "from=2026-09-01&to=2026-02-30", "to" },
        { "from=2026-09-01&to=tomorrow", "to" },
        { "from=9999-12-31&to=9999-12-31", "from" },
        { "from=2026-09-02&to=2026-09-01", "to" },
        { "from=2026-09-01&to=2026-10-02", "to" }, // 32 days inclusive
        { "from=2026-09-01&to=2026-09-01&slotMinutes=4", "slotMinutes" },
        { "from=2026-09-01&to=2026-09-01&slotMinutes=481", "slotMinutes" },
        { "from=2026-09-01&to=2026-09-01&slotMinutes=0", "slotMinutes" },
        { "from=2026-09-01&to=2026-09-01&slotMinutes=-30", "slotMinutes" },
        // Still validated when serviceId makes it unused.
        { $"from=2026-09-01&to=2026-09-01&slotMinutes=1&serviceId={Guid.NewGuid()}", "slotMinutes" },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task Get_InvalidQuery_Returns400InTheErrorShape(string query, string field)
    {
        var businessId = SeedBusiness();

        var response = await factory.CreateClient().GetAsync(Url(businessId, query));

        await AssertBadRequest(response, field);
    }

    [Theory]
    [InlineData("from=2026-09-01&to=2026-09-01&slotMinutes=abc")]
    [InlineData("from=2026-09-01&to=2026-09-01&slotMinutes=30.5")]
    [InlineData("from=2026-09-01&to=2026-09-01&serviceId=not-a-guid")]
    public async Task Get_UnparseableQueryValue_Returns400InTheErrorShape(string query)
    {
        var businessId = SeedBusiness();

        var response = await factory.CreateClient().GetAsync(Url(businessId, query));

        await AssertBadRequest(response, field: null);
    }

    [Fact]
    public async Task Get_MalformedBusinessId_Returns400InTheErrorShape()
    {
        var response = await factory.CreateClient().GetAsync("/booking/businesses/not-a-guid/availability?from=2026-09-01&to=2026-09-01");

        await AssertBadRequest(response, field: "businessId");
    }

    // ---------- 404 ----------

    [Fact]
    public async Task Get_UnknownBusiness_Returns404()
    {
        factory.Repository.Reset();

        var response = await factory.CreateClient().GetAsync(Url(Guid.NewGuid(), "from=2026-09-01&to=2026-09-01"));

        await AssertNotFound(response, "El negocio indicado no existe.");
    }

    [Fact]
    public async Task Get_InactiveBusiness_Returns404()
    {
        var businessId = SeedBusiness(isActive: false);

        var response = await factory.CreateClient().GetAsync(Url(businessId, "from=2026-09-01&to=2026-09-01"));

        await AssertNotFound(response, "El negocio indicado no existe.");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("other business")]
    public async Task Get_ServiceNotUsable_Returns404(string scenario)
    {
        var businessId = SeedBusiness();
        var serviceId = scenario switch
        {
            "missing" => Guid.NewGuid(),
            "inactive" => SeedService(businessId, durationMinutes: 30, isActive: false),
            _ => SeedService(SeedOtherBusiness(), durationMinutes: 30),
        };

        var response = await factory.CreateClient().GetAsync(
            Url(businessId, $"from=2026-09-01&to=2026-09-01&serviceId={serviceId}"));

        await AssertNotFound(response, "El servicio indicado no existe.");
    }

    // ---------- helpers ----------

    private static string Url(Guid businessId, string query) =>
        $"/booking/businesses/{businessId}/availability?{query}";

    private async Task<List<JsonElement>> GetDays(Guid businessId, string query)
    {
        var response = await factory.CreateClient().GetAsync(Url(businessId, query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("days").EnumerateArray().ToList();
    }

    private static IEnumerable<string?> SlotStarts(JsonElement day) =>
        day.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startAt").GetString());

    /// <summary>Resets the fake, then seeds a business open Tuesdays 08:00–10:00 local.</summary>
    private Guid SeedBusiness(bool isActive = true)
    {
        factory.Repository.Reset();
        var businessId = Guid.NewGuid();
        factory.Repository.Businesses.Add(new BusinessReference { Id = businessId, IsActive = isActive });
        factory.Repository.Hours.Add(new BusinessHoursReference
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            DayOfWeek = (short)Day.DayOfWeek,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = new TimeOnly(10, 0),
        });
        return businessId;
    }

    private Guid SeedOtherBusiness()
    {
        var businessId = Guid.NewGuid();
        factory.Repository.Businesses.Add(new BusinessReference { Id = businessId, IsActive = true });
        return businessId;
    }

    private Guid SeedService(Guid businessId, int durationMinutes, bool isActive = true)
    {
        var serviceId = Guid.NewGuid();
        factory.Repository.Services.Add(new ServiceReference
        {
            Id = serviceId, BusinessId = businessId, DurationMinutes = durationMinutes, IsActive = isActive,
        });
        return serviceId;
    }

    private void SeedBooking(Guid businessId, string startUtc, string endUtc, BookingStatus status) =>
        factory.Repository.Bookings.Add((businessId, new BookedInterval(
            DateTimeOffset.Parse(startUtc, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(endUtc, System.Globalization.CultureInfo.InvariantCulture),
            status)));

    private static async Task AssertBadRequest(HttpResponseMessage response, string? field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.True(body.TryGetProperty("timestamp", out _));
        if (field is not null)
            Assert.Equal(field, body.GetProperty("details").GetProperty("field").GetString());
    }

    private static async Task AssertNotFound(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));
    }
}
