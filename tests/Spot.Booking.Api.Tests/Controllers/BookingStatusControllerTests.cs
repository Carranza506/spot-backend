using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Spot.Booking.Api.Models;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Controllers;

/// <summary>
/// The HTTP side of cancel / complete / no-show (#69): routes, roles and error mapping. The
/// transition rules themselves are covered by Services/BookingStatusTransitionTests.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class BookingStatusControllerTests(BookingsApiFactory factory) : IClassFixture<BookingsApiFactory>
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Theory]
    [InlineData("cancel", "CLIENT", "CANCELLED")]
    [InlineData("cancel", "BUSINESS", "CANCELLED")]
    [InlineData("complete", "BUSINESS", "COMPLETED")]
    [InlineData("no-show", "BUSINESS", "NO_SHOW")]
    public async Task Transition_AllowedCaller_Returns200WithTheUpdatedBooking(string action, string role, string expected)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);
        var callerId = role == "CLIENT" ? ClientId : OwnerId;

        var response = await Client(callerId, role).PostAsync($"/booking/bookings/{booking.Id}/{action}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(booking.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(expected, body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_WithAReasonBody_Returns200()
    {
        var booking = await SeedAsync(BookingStatus.PENDING);

        var response = await Client(ClientId, "CLIENT")
            .PostAsJsonAsync($"/booking/bookings/{booking.Id}/cancel", new { reason = "No puedo asistir." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("cancel", BookingStatus.COMPLETED)]
    [InlineData("complete", BookingStatus.PENDING)]
    [InlineData("no-show", BookingStatus.CANCELLED)]
    public async Task Transition_FromAnInvalidStatus_Returns422(string action, BookingStatus from)
    {
        var booking = await SeedAsync(from);

        var response = await Client(OwnerId, "BUSINESS").PostAsync($"/booking/bookings/{booking.Id}/{action}", null);

        await AssertError(response, HttpStatusCode.UnprocessableEntity, "BOOKING_INVALID_STATUS_TRANSITION");
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("no-show")]
    public async Task CompleteOrNoShow_ClientToken_Returns403(string action)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        var response = await Client(ClientId, "CLIENT").PostAsync($"/booking/bookings/{booking.Id}/{action}", null);

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("cancel", "CLIENT")]
    [InlineData("cancel", "BUSINESS")]
    [InlineData("complete", "BUSINESS")]
    public async Task Transition_SomeoneElsesBooking_Returns403(string action, string role)
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        var response = await Client(Guid.NewGuid(), role).PostAsync($"/booking/bookings/{booking.Id}/{action}", null);

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
    }

    [Fact]
    public async Task Cancel_SuperadminToken_Returns403()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        var response = await Client(Guid.NewGuid(), "SUPERADMIN").PostAsync($"/booking/bookings/{booking.Id}/cancel", null);

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Transition_UnknownBooking_Returns404()
    {
        await SeedAsync(BookingStatus.CONFIRMED);

        var response = await Client(OwnerId, "BUSINESS").PostAsync($"/booking/bookings/{Guid.NewGuid()}/cancel", null);

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Transition_WithoutToken_Returns401()
    {
        var booking = await SeedAsync(BookingStatus.CONFIRMED);

        var response = await factory.CreateClient().PostAsync($"/booking/bookings/{booking.Id}/cancel", null);

        await AssertError(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    /// <summary>Resets the fakes, then seeds one booking of <see cref="ClientId"/> at a business owned by <see cref="OwnerId"/>.</summary>
    private async Task<BookingEntity> SeedAsync(BookingStatus status)
    {
        factory.ScheduleRepository.Reset();
        factory.BookingRepository.Reset();

        var businessId = Guid.NewGuid();
        factory.ScheduleRepository.Businesses.Add(new BusinessReference { Id = businessId, AccountId = OwnerId, IsActive = true });

        var start = new DateTimeOffset(2026, 9, 1, 14, 0, 0, TimeSpan.Zero);
        var booking = new BookingEntity
        {
            UserId = ClientId,
            BusinessId = businessId,
            ServiceId = Guid.NewGuid(),
            StartAt = start,
            EndAt = start.AddMinutes(45),
            Status = status,
            ServiceName = "Corte de cabello",
            ServicePrice = 12000.50m,
            ServiceDurationMinutes = 45,
        };
        await factory.BookingRepository.CreateAsync(booking);
        return booking;
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
    }
}
