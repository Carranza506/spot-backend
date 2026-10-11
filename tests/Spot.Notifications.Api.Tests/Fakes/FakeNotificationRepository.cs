using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;

namespace Spot.Notifications.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for INotificationRepository — no database involved. Mirrors the real
/// repository's filter, order (newest first, id as tie-breaker) and paging, so callers see the
/// same pages whether they went through the fake or the Postgres-backed one.
/// </summary>
public sealed class FakeNotificationRepository : INotificationRepository
{
    private readonly Dictionary<Guid, Notification> _notifications = [];

    public IReadOnlyCollection<Notification> All => _notifications.Values;

    public void Reset() => _notifications.Clear();

    /// <summary>Seeds a notification directly — nothing in the API creates them yet.</summary>
    public Notification Seed(Notification notification)
    {
        if (notification.Id == Guid.Empty)
            notification.Id = Guid.NewGuid();

        if (notification.CreatedAt == default)
            notification.CreatedAt = DateTimeOffset.UtcNow;

        _notifications[notification.Id] = notification;
        return notification;
    }

    public Task<(IReadOnlyList<Notification> Items, int Total)> ListByUserAsync(
        Guid userId, bool? isRead, int page, int pageSize, CancellationToken ct = default)
    {
        var matches = _notifications.Values.Where(x => x.UserId == userId);
        if (isRead.HasValue)
            matches = matches.Where(x => x.IsRead == isRead.Value);

        var all = matches.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToList();
        var offset = (long)(page - 1) * pageSize;
        var paged = offset >= all.Count ? [] : all.Skip((int)offset).Take(pageSize).ToList();

        return Task.FromResult(((IReadOnlyList<Notification>)paged, all.Count));
    }

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_notifications.GetValueOrDefault(id));

    public Task SetReadAsync(Notification notification, bool isRead, CancellationToken ct = default)
    {
        notification.IsRead = isRead;
        _notifications[notification.Id] = notification;
        return Task.CompletedTask;
    }
}
