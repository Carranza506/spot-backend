using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Services;
using Spot.Notifications.Api.Tests.Fakes;

namespace Spot.Notifications.Api.Tests.Services;

public class NotificationServiceTests
{
    private readonly FakeNotificationRepository _repository = new();
    private readonly NotificationService _service;

    public NotificationServiceTests() => _service = new NotificationService(_repository, new FakePushNotificationService());

    [Fact]
    public async Task ListAsync_MapsEveryFieldAndWrapsPaginationMetadata()
    {
        var userId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var seeded = _repository.Seed(new Notification
        {
            UserId = userId,
            Type = NotificationType.BOOKING_STATUS_CHANGED,
            Title = "Tu reservación fue confirmada",
            Body = "Salón Bella Vista confirmó tu cita.",
            RelatedEntityType = "booking",
            RelatedEntityId = relatedId,
            IsRead = false,
            CreatedAt = createdAt,
        });
        _repository.Seed(new Notification { UserId = userId, Type = NotificationType.GENERAL, Title = "a", Body = "b", CreatedAt = createdAt.AddMinutes(-1) });
        _repository.Seed(new Notification { UserId = userId, Type = NotificationType.GENERAL, Title = "a", Body = "b", CreatedAt = createdAt.AddMinutes(-2) });

        var result = await _service.ListAsync(userId, isRead: null, page: 1, pageSize: 2);

        Assert.Equal(1, result.Pagination.Page);
        Assert.Equal(2, result.Pagination.PageSize);
        Assert.Equal(3, result.Pagination.Total);
        Assert.Equal(2, result.Pagination.TotalPages);
        Assert.Equal(2, result.Data.Count);

        var dto = result.Data[0];
        Assert.Equal(seeded.Id, dto.Id);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal("BOOKING_STATUS_CHANGED", dto.Type);
        Assert.Equal("Tu reservación fue confirmada", dto.Title);
        Assert.Equal("Salón Bella Vista confirmó tu cita.", dto.Body);
        Assert.Equal("booking", dto.RelatedEntityType);
        Assert.Equal(relatedId, dto.RelatedEntityId);
        Assert.False(dto.IsRead);
        Assert.Equal(createdAt, dto.CreatedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetReadAsync_Owner_UpdatesAndReturnsTheNotification(bool isRead)
    {
        var userId = Guid.NewGuid();
        var notification = _repository.Seed(new Notification
        {
            UserId = userId, Type = NotificationType.GENERAL, Title = "a", Body = "b", IsRead = !isRead,
        });

        var result = await _service.SetReadAsync(userId, notification.Id, isRead);

        Assert.Equal(NotificationUpdateOutcome.Updated, result.Outcome);
        Assert.Equal(isRead, notification.IsRead);
        Assert.Equal(notification.Id, result.Notification!.Id);
        Assert.Equal(isRead, result.Notification.IsRead);
    }

    [Fact]
    public async Task SetReadAsync_SomeoneElsesNotification_IsForbiddenAndUnchanged()
    {
        var notification = _repository.Seed(new Notification
        {
            UserId = Guid.NewGuid(), Type = NotificationType.GENERAL, Title = "a", Body = "b", IsRead = false,
        });

        var result = await _service.SetReadAsync(Guid.NewGuid(), notification.Id, isRead: true);

        Assert.Equal(NotificationUpdateOutcome.Forbidden, result.Outcome);
        Assert.Null(result.Notification);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task SetReadAsync_UnknownId_IsNotFound()
    {
        var result = await _service.SetReadAsync(Guid.NewGuid(), Guid.NewGuid(), isRead: true);

        Assert.Equal(NotificationUpdateOutcome.NotFound, result.Outcome);
        Assert.Null(result.Notification);
    }
}
