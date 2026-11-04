using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Options;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Services;

/// <summary>
/// Posts to triggerBookingStatusNotification through the Gateway's internal port, the only one
/// that routes it.
/// </summary>
public sealed class BookingStatusNotifier(
    HttpClient httpClient,
    IOptions<InternalApiOptions> options,
    ILogger<BookingStatusNotifier> logger) : IBookingStatusNotifier
{
    private const string EndpointPath = "api/v1/notifications/internal/booking-status-changed";
    private const string ApiKeyHeader = "X-Internal-Api-Key";

    public async Task NotifyAsync(BookingEntity booking)
    {
        var settings = options.Value;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.GatewayBaseUrl.TrimEnd('/')}/{EndpointPath}")
            {
                Content = JsonContent.Create(new InternalBookingStatusNotificationRequest(
                    booking.Id, booking.BusinessId, booking.UserId, booking.Status)),
            };
            request.Headers.Add(ApiKeyHeader, settings.Key);

            using var response = await httpClient.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning(
                    "Status notification for booking {BookingId} ({Status}) was rejected with {StatusCode}.",
                    booking.Id, booking.Status, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Everything, a timeout or a missing setting included: the transition is already
            // committed. Only the exception type, since its message could carry the key.
            logger.LogWarning(
                "Status notification for booking {BookingId} ({Status}) failed ({ExceptionType}).",
                booking.Id, booking.Status, ex.GetType().Name);
        }
    }
}
