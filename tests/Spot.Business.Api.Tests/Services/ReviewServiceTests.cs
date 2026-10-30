using Spot.Business.Api.DTOs;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Business.Api.Tests.Fakes;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Services;

/// <summary>ReviewService's rules (#70), on the in-memory fakes.</summary>
public sealed class ReviewServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeBusinessRepository _businesses = new();
    private readonly FakeReviewRepository _reviews = new();
    private readonly ReviewService _service;

    public ReviewServiceTests() => _service = new ReviewService(_businesses, _reviews);

    [Fact]
    public async Task CreateAsync_TakesTheUserAndBusinessFromTheBooking()
    {
        var clientId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var booking = _reviews.SeedBooking(clientId, businessId);

        var result = await _service.CreateAsync(booking.Id, clientId, new ReviewCreateRequest { Rating = 4, Comment = " Bien " });

        Assert.Equal((booking.Id, clientId, businessId, 4, "Bien"), (result.BookingId, result.UserId, result.BusinessId, result.Rating, result.Comment));
        Assert.Single(_reviews.All);
    }

    [Fact]
    public async Task CreateAsync_UnknownBooking_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<BookingNotFoundException>(
            () => _service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), new ReviewCreateRequest { Rating = 5 }));
    }

    [Fact]
    public async Task CreateAsync_NotTheBookingsClient_ThrowsAccessDeniedAndCreatesNothing()
    {
        var booking = _reviews.SeedBooking(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<BookingAccessDeniedException>(
            () => _service.CreateAsync(booking.Id, Guid.NewGuid(), new ReviewCreateRequest { Rating = 5 }));
        Assert.Empty(_reviews.All);
    }

    [Fact]
    public async Task CreateAsync_NotCompleted_ThrowsNotReviewable()
    {
        var clientId = Guid.NewGuid();
        var booking = _reviews.SeedBooking(clientId, Guid.NewGuid(), completed: false);

        await Assert.ThrowsAsync<BookingNotReviewableException>(
            () => _service.CreateAsync(booking.Id, clientId, new ReviewCreateRequest { Rating = 5 }));
    }

    [Fact]
    public async Task CreateAsync_AlreadyReviewed_ThrowsAlreadyExists()
    {
        var clientId = Guid.NewGuid();
        var booking = _reviews.SeedBooking(clientId, Guid.NewGuid());
        await _service.CreateAsync(booking.Id, clientId, new ReviewCreateRequest { Rating = 5 });

        await Assert.ThrowsAsync<ReviewAlreadyExistsException>(
            () => _service.CreateAsync(booking.Id, clientId, new ReviewCreateRequest { Rating = 1 }));
    }

    [Fact]
    public async Task ListAsync_UnknownOrInactiveBusiness_ReturnsNull()
    {
        var inactive = _businesses.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Cerrado", IsActive = false });

        Assert.Null(await _service.ListAsync(Guid.NewGuid(), 1, 20));
        Assert.Null(await _service.ListAsync(inactive.Id, 1, 20));
    }

    [Fact]
    public async Task ListAsync_ReturnsThePageAndItsPagination()
    {
        var business = _businesses.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        for (var i = 0; i < 3; i++)
            _reviews.SeedReview(business.Id, 5, T0.AddDays(i));

        var result = await _service.ListAsync(business.Id, page: 1, pageSize: 2);

        Assert.Equal(2, result!.Data.Count);
        Assert.Equal((1, 2, 3, 2), (result.Pagination.Page, result.Pagination.PageSize, result.Pagination.Total, result.Pagination.TotalPages));
    }
}
