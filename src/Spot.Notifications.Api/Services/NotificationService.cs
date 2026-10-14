using Spot.Notifications.Api.DTOs;
using Spot.Notifications.Api.Repositories;
using Spot.Shared.Pagination;

namespace Spot.Notifications.Api.Services;

public sealed class NotificationService(INotificationRepository repository) : INotificationService
{
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
}
