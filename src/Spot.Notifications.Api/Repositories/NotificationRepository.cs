using Microsoft.EntityFrameworkCore;
using Spot.Notifications.Api.Data;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Repositories;

public sealed class NotificationRepository(NotificationsDbContext db) : INotificationRepository
{
    public async Task<(IReadOnlyList<Notification> Items, int Total)> ListByUserAsync(
        Guid userId, bool? isRead, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Notifications.AsNoTracking().Where(x => x.UserId == userId);

        if (isRead.HasValue)
            query = query.Where(x => x.IsRead == isRead.Value);

        var total = await query.CountAsync(ct);

        // Offset as long so a huge page can't overflow int into a negative OFFSET (a 500 on
        // Postgres); past the last row there's nothing to return anyway.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
            return ([], total);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            // Tie-breaker: notifications created in the same instant must not swap pages.
            .ThenByDescending(x => x.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task SetReadAsync(Notification notification, bool isRead, CancellationToken ct = default)
    {
        notification.IsRead = isRead;
        return db.SaveChangesAsync(ct);
    }

    public Task AddRangeAsync(IReadOnlyList<Notification> notifications, CancellationToken ct = default)
    {
        db.Notifications.AddRange(notifications);
        return db.SaveChangesAsync(ct);
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default) =>
        db.Set<UserReference>().AnyAsync(x => x.Id == userId, ct);

    public Task<Guid?> GetBusinessAccountIdAsync(Guid businessId, CancellationToken ct = default) =>
        db.Set<BusinessReference>()
            .Where(x => x.Id == businessId)
            .Select(x => (Guid?)x.AccountId)
            .FirstOrDefaultAsync(ct);
}
