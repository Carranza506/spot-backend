using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.Services;

/// <summary>
/// The availability rules of #66, kept pure: no database, clock or time zone lookup — everything
/// comes in as arguments, so the calculation can be unit tested on its own.
/// </summary>
public static class AvailabilityCalculator
{
    /// <summary>Bookings with these statuses take up their time; any other status is ignored (same set as excl_active_booking_overlap).</summary>
    public static readonly IReadOnlySet<BookingStatus> BlockingStatuses =
        new HashSet<BookingStatus> { BookingStatus.PENDING, BookingStatus.CONFIRMED };

    /// <summary>
    /// One <see cref="AvailabilityDay"/> per local date in [<paramref name="from"/>, <paramref name="to"/>],
    /// in order. A date's schedule exception overrides the weekly hours for its day of week; a date
    /// with neither is closed. An open day gets back-to-back slots of <paramref name="slotMinutes"/>
    /// from its opening time, keeping only those that end by its closing time, don't overlap a
    /// blocking booking (half-open intervals) and don't start before <paramref name="now"/>.
    /// </summary>
    public static IReadOnlyList<AvailabilityDay> Calculate(
        DateOnly from,
        DateOnly to,
        int slotMinutes,
        IEnumerable<BusinessHoursReference> weeklyHours,
        IEnumerable<BusinessScheduleExceptionReference> exceptions,
        IEnumerable<BookedInterval> bookings,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slotMinutes, 1);

        var hoursByDay = weeklyHours.ToDictionary(h => h.DayOfWeek);
        var exceptionsByDate = exceptions.ToDictionary(e => e.ExceptionDate);
        var blocked = bookings
            .Where(b => BlockingStatuses.Contains(b.Status))
            .Select(b => (Start: b.StartAt.UtcDateTime, End: b.EndAt.UtcDateTime))
            .ToList();
        var nowUtc = now.UtcDateTime;
        var length = TimeSpan.FromMinutes(slotMinutes);

        var days = new List<AvailabilityDay>();
        for (var i = 0; i <= to.DayNumber - from.DayNumber; i++)
        {
            var date = from.AddDays(i);
            var opening = OpeningHoursResolver.Resolve(
                hoursByDay.GetValueOrDefault((short)date.DayOfWeek), exceptionsByDate.GetValueOrDefault(date));
            if (opening is not { } hours)
            {
                days.Add(new AvailabilityDay(date, IsClosed: true, []));
                continue;
            }

            var closeUtc = ToUtc(date, hours.Close, timeZone);
            var slots = new List<AvailabilitySlot>();
            for (var start = ToUtc(date, hours.Open, timeZone); start + length <= closeUtc; start += length)
            {
                var end = start + length;
                if (start < nowUtc || blocked.Any(b => start < b.End && b.Start < end))
                    continue;

                slots.Add(new AvailabilitySlot(start, end));
            }

            // Open but fully booked (or already past) is still an open day, just without slots.
            days.Add(new AvailabilityDay(date, IsClosed: false, slots));
        }

        return days;
    }

    /// <summary>The UTC instant (Kind = Utc) of <paramref name="time"/> on the local <paramref name="date"/> in <paramref name="timeZone"/>.</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), timeZone);
}
