using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;
using Spot.Notifications.Api.Services.Push;
using Spot.Shared.Pagination;

namespace Spot.Notifications.Api.Services;

public sealed class NotificationService(
    INotificationRepository repository,
    IPushNotificationService pushNotifications,
    ILogger<NotificationService> logger) : INotificationService
{
    /// <summary>notifications.related_entity_type for bookings, as in the contract's Notification example.</summary>
    private const string BookingEntityType = "booking";

    public async Task<PaginatedResponse<NotificationDto>> ListAsync(
        Guid userId, bool? isRead, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await repository.ListByUserAsync(userId, isRead, page, pageSize, ct);

        var dtos = items.Select(NotificationDto.FromEntity).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new PaginatedResponse<NotificationDto>(dtos, new PaginationMeta(page, pageSize, total, totalPages));
    }

    public async Task<NotificationUpdateResult> SetReadAsync(
        Guid userId, Guid notificationId, bool isRead, CancellationToken ct = default)
    {
        var notification = await repository.GetByIdAsync(notificationId, ct);
        if (notification is null)
            return new NotificationUpdateResult(NotificationUpdateOutcome.NotFound);

        if (notification.UserId != userId)
            return new NotificationUpdateResult(NotificationUpdateOutcome.Forbidden);

        await repository.SetReadAsync(notification, isRead, ct);
        return new NotificationUpdateResult(NotificationUpdateOutcome.Updated, NotificationDto.FromEntity(notification));
    }

    public async Task<BookingStatusNotificationResult> NotifyBookingStatusChangedAsync(
        InternalBookingStatusNotificationRequest request, CancellationToken ct = default)
    {
        var userId = request.UserId!.Value;
        var bookingId = request.BookingId!.Value;
        var status = request.Status!.Value;

        if (!await repository.UserExistsAsync(userId, ct))
            return BookingStatusNotificationResult.UserNotFound;

        if (await repository.GetBusinessAccountIdAsync(request.BusinessId!.Value, ct) is not { } businessAccountId)
            return BookingStatusNotificationResult.BusinessNotFound;

        var (title, defaultBody) = DefaultTextFor(status);
        var body = string.IsNullOrWhiteSpace(request.Message) ? defaultBody : request.Message;

        Guid[] recipients = status == BookingStatus.CANCELLED ? [userId, businessAccountId] : [userId];

        // Not the request's token, here or on the pushes below: the booking already changed, so
        // Booking giving up on its short timeout must not cost the notification its row or delivery.
        await repository.AddRangeAsync(recipients.Select(recipient => new Notification
        {
            UserId = recipient,
            Type = NotificationType.BOOKING_STATUS_CHANGED,
            Title = title,
            Body = body,
            RelatedEntityType = BookingEntityType,
            RelatedEntityId = bookingId,
        }).ToList(), CancellationToken.None);

        var data = new Dictionary<string, string>
        {
            ["bookingId"] = bookingId.ToString(),
            ["status"] = status.ToString(),
        };

        foreach (var recipient in recipients)
        {
            try
            {
                await pushNotifications.SendToUserAsync(recipient, title, body, data, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // One recipient's failure must not cost the next one their push, nor the 202: the
                // rows are stored. Only the exception type: its message could carry a device token.
                logger.LogWarning(
                    "Push to user {UserId} for booking {BookingId} failed ({ExceptionType}).",
                    recipient, bookingId, ex.GetType().Name);
            }
        }

        return BookingStatusNotificationResult.Accepted;
    }

    private static (string Title, string Body) DefaultTextFor(BookingStatus status) => status switch
    {
        BookingStatus.CONFIRMED => ("Tu reservación fue confirmada", "El negocio confirmó tu reservación."),
        BookingStatus.COMPLETED => ("Reservación completada", "Tu reservación fue marcada como completada. ¡Gracias por usar Spot!"),
        // Neutral on purpose: this one goes to both the client and the business account.
        BookingStatus.CANCELLED => ("Reservación cancelada", "La reservación fue cancelada."),
        BookingStatus.NO_SHOW => ("Reservación no asistida", "Tu reservación fue marcada como no asistida."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a status change."),
    };
}
