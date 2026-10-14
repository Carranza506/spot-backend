using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Repositories;

public interface INotificationRepository
{
    /// <summary>
    /// One page of <paramref name="userId"/>'s notifications, newest first, plus the total count.
    /// <paramref name="isRead"/> filters by read state when given; null means all.
    /// </summary>
    Task<(IReadOnlyList<Notification> Items, int Total)> ListByUserAsync(
        Guid userId, bool? isRead, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Loads a notification by id regardless of owner, or null if none exists — the caller does
    /// the ownership check, same as <see cref="IDeviceTokenRepository.GetByIdAsync"/>.
    /// </summary>
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sets <paramref name="notification"/>'s read state and persists it.</summary>
    Task SetReadAsync(Notification notification, bool isRead, CancellationToken ct = default);
}
