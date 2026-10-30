using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Options;
using Spot.Booking.Api.Services;
using Spot.Booking.Api.Tests.Fakes;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Services;

/// <summary>The real notifier over a fake network, alone and behind the real BookingService.</summary>
public class BookingStatusNotifierTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static readonly InternalApiOptions Settings = new()
    {
        GatewayBaseUrl = "http://gateway:8081/",
        Key = "test-key",
        TimeoutSeconds = 1,
    };

    private static readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] Failures =
    [
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
        (_, _) => throw new HttpRequestException("connection refused"),
        async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        },
    ];

    public static TheoryData<int> FailureCases => new(Enumerable.Range(0, Failures.Length));

    [Fact]
    public async Task Posts_TheContractBody_ToTheInternalAddress_WithTheKey()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var notifier = CreateNotifier(async (request, ct) =>
        {
            sent = request;
            body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }, new CapturingLogger());
        var booking = NewBooking(BookingStatus.CANCELLED);

        await notifier.NotifyAsync(booking);

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal(
            "http://gateway:8081/api/v1/notifications/internal/booking-status-changed",
            sent.RequestUri!.ToString());
        Assert.Equal("test-key", Assert.Single(sent.Headers.GetValues("X-Internal-Api-Key")));

        var json = JsonDocument.Parse(body!).RootElement;
        Assert.Equal(
            ["bookingId", "businessId", "userId", "status"],
            json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(booking.Id, json.GetProperty("bookingId").GetGuid());
        Assert.Equal(booking.BusinessId, json.GetProperty("businessId").GetGuid());
        Assert.Equal(booking.UserId, json.GetProperty("userId").GetGuid());
        Assert.Equal("CANCELLED", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Success_LogsNoWarning()
    {
        var logger = new CapturingLogger();
        var notifier = CreateNotifier((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)), logger);

        await notifier.NotifyAsync(NewBooking(BookingStatus.CANCELLED));

        Assert.Equal(0, logger.Warnings);
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task AnyFailure_IsLoggedAsAWarning_AndNeverThrown(int failure)
    {
        var logger = new CapturingLogger();
        var notifier = CreateNotifier(Failures[failure], logger);

        await notifier.NotifyAsync(NewBooking(BookingStatus.CANCELLED));

        Assert.Equal(1, logger.Warnings);
    }

    [Fact]
    public async Task MissingConfiguration_IsLoggedAsAWarning_AndNeverThrown()
    {
        var logger = new CapturingLogger();
        var notifier = new BookingStatusNotifier(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new InternalApiOptions()), logger);

        await notifier.NotifyAsync(NewBooking(BookingStatus.CANCELLED));

        Assert.Equal(1, logger.Warnings);
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task TheTransitionStillSucceeds_AndIsCommitted_WhenTheNotificationFails(int failure)
    {
        var schedule = new FakeAvailabilityRepository();
        var bookings = new FakeBookingRepository(schedule);
        var notifier = CreateNotifier(Failures[failure], new CapturingLogger());
        var service = new BookingService(schedule, bookings, TimeProvider.System, notifier);

        var businessId = Guid.NewGuid();
        schedule.Businesses.Add(new BusinessReference { Id = businessId, AccountId = OwnerId, IsActive = true });
        var booking = new BookingEntity
        {
            UserId = ClientId,
            BusinessId = businessId,
            ServiceId = Guid.NewGuid(),
            StartAt = DateTimeOffset.UtcNow.AddDays(1),
            EndAt = DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30),
            Status = BookingStatus.CONFIRMED,
            ServiceName = "Corte",
            ServicePrice = 10_000m,
            ServiceDurationMinutes = 30,
        };
        await bookings.CreateAsync(booking);

        var result = await service.CancelAsync(booking.Id, ClientId);

        Assert.Equal(BookingStatus.CANCELLED, result.Status);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
    }

    private static BookingStatusNotifier CreateNotifier(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, ILogger<BookingStatusNotifier> logger) =>
        new(new HttpClient(new FakeHandler(send)), Microsoft.Extensions.Options.Options.Create(Settings), logger);

    private static BookingEntity NewBooking(BookingStatus status) => new()
    {
        Id = Guid.NewGuid(),
        UserId = ClientId,
        BusinessId = Guid.NewGuid(),
        Status = status,
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class CapturingLogger : ILogger<BookingStatusNotifier>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings++;
        }
    }
}
