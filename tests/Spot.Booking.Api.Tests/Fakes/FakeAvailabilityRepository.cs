using Spot.Booking.Api.Models;
using Spot.Booking.Api.Repositories;
using Spot.Booking.Api.Services;

namespace Spot.Booking.Api.Tests.Fakes;

/// <summary>
/// In-memory stand-in for IAvailabilityRepository — no database involved. Mirrors the real
/// repository's filters: business scoping, the inclusive exception date range, and only active
/// (PENDING/CONFIRMED) bookings overlapping the half-open UTC range.
/// </summary>
public sealed class FakeAvailabilityRepository : IAvailabilityRepository
{
    public List<BusinessReference> Businesses { get; } = [];
    public List<ServiceReference> Services { get; } = [];
    public List<BusinessHoursReference> Hours { get; } = [];
    public List<BusinessScheduleExceptionReference> Exceptions { get; } = [];
    public List<(Guid BusinessId, BookedInterval Booking)> Bookings { get; } = [];

    public void Reset()
    {
        Businesses.Clear();
        Services.Clear();
        Hours.Clear();
        Exceptions.Clear();
        Bookings.Clear();
    }

    public Task<BusinessReference?> GetBusinessAsync(Guid businessId, CancellationToken ct = default) =>
        Task.FromResult(Businesses.FirstOrDefault(b => b.Id == businessId));

    public Task<ServiceReference?> GetServiceAsync(Guid serviceId, CancellationToken ct = default) =>
        Task.FromResult(Services.FirstOrDefault(s => s.Id == serviceId));

    public Task<IReadOnlyList<BusinessHoursReference>> GetWeeklyHoursAsync(Guid businessId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BusinessHoursReference>>(Hours.Where(h => h.BusinessId == businessId).ToList());

    public Task<IReadOnlyList<BusinessScheduleExceptionReference>> GetScheduleExceptionsAsync(
        Guid businessId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BusinessScheduleExceptionReference>>(Exceptions
            .Where(e => e.BusinessId == businessId && e.ExceptionDate >= from && e.ExceptionDate <= to)
            .ToList());

    public Task<IReadOnlyList<BookedInterval>> GetActiveBookingsAsync(
        Guid businessId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BookedInterval>>(Bookings
            .Where(x => x.BusinessId == businessId
                && AvailabilityCalculator.BlockingStatuses.Contains(x.Booking.Status)
                && x.Booking.StartAt < endUtc
                && x.Booking.EndAt > startUtc)
            .Select(x => x.Booking)
            .ToList());
}
