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

    /// <summary>Stores all of <paramref name="notifications"/> in one transaction.</summary>
    Task AddRangeAsync(IReadOnlyList<Notification> notifications, CancellationToken ct = default);

    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>businesses.account_id of <paramref name="businessId"/>, or null if no such business exists.</summary>
    Task<Guid?> GetBusinessAccountIdAsync(Guid businessId, CancellationToken ct = default);
}
