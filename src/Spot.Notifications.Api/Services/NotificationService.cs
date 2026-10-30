using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;
using Spot.Notifications.Api.Services.Push;
using Spot.Shared.Pagination;

namespace Spot.Notifications.Api.Services;

public sealed class NotificationService(
    INotificationRepository repository,
    IPushNotificationService pushNotifications) : INotificationService
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

        await repository.AddRangeAsync(recipients.Select(recipient => new Notification
        {
            UserId = recipient,
            Type = NotificationType.BOOKING_STATUS_CHANGED,
            Title = title,
            Body = body,
            RelatedEntityType = BookingEntityType,
            RelatedEntityId = bookingId,
        }).ToList(), ct);

        var data = new Dictionary<string, string>
        {
            ["bookingId"] = bookingId.ToString(),
            ["status"] = status.ToString(),
        };

        // Not the request's token: the rows are already stored, so Booking giving up on its short
        // timeout must not cut the delivery short.
        foreach (var recipient in recipients)
            await pushNotifications.SendToUserAsync(recipient, title, body, data, CancellationToken.None);

        return BookingStatusNotificationResult.Accepted;
    }

    private static (string Title, string Body) DefaultTextFor(BookingStatus status) => status switch
    {
        BookingStatus.CONFIRMED => ("Booking confirmed", "Your booking has been confirmed."),
        BookingStatus.COMPLETED => ("Booking completed", "Your booking has been completed."),
        BookingStatus.CANCELLED => ("Booking cancelled", "This booking has been cancelled."),
        BookingStatus.NO_SHOW => ("Booking marked as no-show", "This booking was marked as a no-show."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a status change."),
    };
}
