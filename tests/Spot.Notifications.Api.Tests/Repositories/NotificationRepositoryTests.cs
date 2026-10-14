using Microsoft.EntityFrameworkCore;
using Spot.Notifications.Api.Data;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;

namespace Spot.Notifications.Api.Tests.Repositories;

/// <summary>
/// Real NotificationRepository queries and SaveChanges round-trips on EF Core's InMemory provider,
/// through <see cref="InMemoryNotificationsDbContext"/>. The notification_type enum mapping and the
/// index can't be exercised here (InMemory has no Postgres types); those were checked against the
/// generated migration SQL.
/// </summary>
public sealed class NotificationRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly DbContextOptions<NotificationsDbContext> _options;
    private readonly NotificationsDbContext _db;
    private readonly NotificationRepository _repository;

    public NotificationRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new InMemoryNotificationsDbContext(_options);
        _repository = new NotificationRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ListByUserAsync_ReturnsOnlyTheUsersNotifications_NewestFirst()
    {
        var userId = Guid.NewGuid();
        var oldest = await SeedAsync(userId, T0);
        var newest = await SeedAsync(userId, T0.AddMinutes(2));
        var middle = await SeedAsync(userId, T0.AddMinutes(1));
        await SeedAsync(Guid.NewGuid(), T0.AddMinutes(5));

        var (items, total) = await _repository.ListByUserAsync(userId, null, page: 1, pageSize: 20);

        Assert.Equal(3, total);
        Assert.Equal([newest.Id, middle.Id, oldest.Id], items.Select(x => x.Id));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(null, 3)]
    public async Task ListByUserAsync_IsReadFilter_NullMeansAll(bool? isRead, int expected)
    {
        var userId = Guid.NewGuid();
        await SeedAsync(userId, T0, isRead: true);
        await SeedAsync(userId, T0.AddMinutes(1), isRead: false);
        await SeedAsync(userId, T0.AddMinutes(2), isRead: false);

        var (items, total) = await _repository.ListByUserAsync(userId, isRead, page: 1, pageSize: 20);

        Assert.Equal(expected, total);
        Assert.Equal(expected, items.Count);
        if (isRead.HasValue)
            Assert.All(items, x => Assert.Equal(isRead.Value, x.IsRead));
    }

    [Fact]
    public async Task ListByUserAsync_Paginates_WithoutRepeatingOrSkippingRows()
    {
        var userId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await SeedAsync(userId, T0.AddMinutes(i));

        var (page1, total) = await _repository.ListByUserAsync(userId, null, page: 1, pageSize: 2);
        var (page2, _) = await _repository.ListByUserAsync(userId, null, page: 2, pageSize: 2);
        var (page3, _) = await _repository.ListByUserAsync(userId, null, page: 3, pageSize: 2);

        Assert.Equal(5, total);
        Assert.Equal([2, 2, 1], new[] { page1.Count, page2.Count, page3.Count });
        var ids = page1.Concat(page2).Concat(page3).Select(x => x.Id).ToList();
        Assert.Equal(5, ids.Distinct().Count());
        // Newest first across page boundaries.
        var created = page1.Concat(page2).Concat(page3).Select(x => x.CreatedAt).ToList();
        Assert.Equal(created.OrderByDescending(x => x), created);
    }

    [Fact]
    public async Task ListByUserAsync_SameCreatedAt_OrdersByIdDescending()
    {
        // Inserted in ascending id order, so insertion order alone would give the opposite result.
        var userId = Guid.NewGuid();
        var low = await SeedAsync(userId, T0, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = await SeedAsync(userId, T0, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));

        var (page1, _) = await _repository.ListByUserAsync(userId, null, page: 1, pageSize: 1);
        var (page2, _) = await _repository.ListByUserAsync(userId, null, page: 2, pageSize: 1);

        Assert.Equal(high.Id, Assert.Single(page1).Id);
        Assert.Equal(low.Id, Assert.Single(page2).Id);
    }

    [Fact]
    public async Task ListByUserAsync_HugePage_ReturnsEmptyWithoutOverflowing()
    {
        var userId = Guid.NewGuid();
        await SeedAsync(userId, T0);

        // (page-1)*pageSize would overflow int into a negative OFFSET on Postgres.
        var (items, total) = await _repository.ListByUserAsync(userId, null, page: 107374184, pageSize: 20);

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SetReadAsync_PersistsTheNewReadState()
    {
        var notification = await SeedAsync(Guid.NewGuid(), T0, isRead: false);
        var tracked = await _repository.GetByIdAsync(notification.Id);

        await _repository.SetReadAsync(tracked!, isRead: true);

        await using var fresh = new InMemoryNotificationsDbContext(_options);
        Assert.True((await fresh.Notifications.SingleAsync(x => x.Id == notification.Id)).IsRead);
    }

    private async Task<Notification> SeedAsync(Guid userId, DateTimeOffset createdAt, bool isRead = false, Guid? id = null)
    {
        await using var db = new InMemoryNotificationsDbContext(_options);
        var notification = new Notification
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId,
            Type = NotificationType.GENERAL,
            Title = "Título",
            Body = "Cuerpo",
            IsRead = isRead,
            CreatedAt = createdAt,
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    /// <summary>
    /// NotificationsDbContext minus AuditLog, whose JsonDocument columns make the InMemory provider
    /// reject the whole model. Test-only; the production context is unchanged.
    /// </summary>
    private sealed class InMemoryNotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : NotificationsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<AuditLog>();
        }
    }
}
