using Spot.Notifications.Api.DTOs;
using Spot.Shared.Pagination;

namespace Spot.Notifications.Api.Services;

public interface INotificationService
{
    /// <summary>
    /// <paramref name="userId"/>'s own notifications, newest first. Callers validate
    /// <paramref name="page"/>/<paramref name="pageSize"/> before calling.
    /// </summary>
    Task<PaginatedResponse<NotificationDto>> ListAsync(
        Guid userId, bool? isRead, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Sets the read state of <paramref name="notificationId"/> on behalf of
    /// <paramref name="userId"/>. See <see cref="NotificationUpdateOutcome"/> for the outcomes.
    /// </summary>
    Task<NotificationUpdateResult> SetReadAsync(
        Guid userId, Guid notificationId, bool isRead, CancellationToken ct = default);
}

public enum NotificationUpdateOutcome
{
    Updated,
    NotFound,

    /// <summary>The notification exists but belongs to a different user.</summary>
    Forbidden,
}

/// <param name="Notification">The updated notification; only set when <paramref name="Outcome"/> is Updated.</param>
public sealed record NotificationUpdateResult(NotificationUpdateOutcome Outcome, NotificationDto? Notification = null);
