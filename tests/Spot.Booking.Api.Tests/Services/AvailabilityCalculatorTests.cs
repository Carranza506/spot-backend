using System.Globalization;
using Spot.Booking.Api.DTOs;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Services;

namespace Spot.Booking.Api.Tests.Services;

/// <summary>
/// The pure availability rules (#66). Times in the schedule are America/Costa_Rica local time
/// (UTC-6, no DST), so 08:00 local is 14:00Z; slot starts are asserted as UTC "HH:mm".
/// </summary>
public class AvailabilityCalculatorTests
{
    // 2026-09-01 is a Tuesday; days of week are always derived from the date, never hard-coded.
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateTimeOffset LongAgo = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------- schedule resolution ----------

    [Fact]
    public void LocalScheduleTimes_AreReturnedAsUtc()
    {
        var day = Calc(Day, Day, 60, hours: [Weekly(Day.DayOfWeek, "09:00", "10:00")]).Single();

        var slot = Assert.Single(day.Slots);
        Assert.Equal(new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc), slot.StartAt);
        Assert.Equal(new DateTime(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc), slot.EndAt);
        Assert.Equal(DateTimeKind.Utc, slot.StartAt.Kind);
    }

    [Fact]
    public void WeeklyHoursOnly_OneDayPerDateInOrder_OpenOnlyOnTheScheduledWeekday()
    {
        var days = Calc(Day, Day.AddDays(6), 60, hours: [Weekly(Day.DayOfWeek, "08:00", "10:00")]);

        Assert.Equal(Enumerable.Range(0, 7).Select(Day.AddDays), days.Select(d => d.Date));
        Assert.False(days[0].IsClosed);
        Assert.Equal(["14:00", "15:00"], Starts(days[0]));
        Assert.All(days.Skip(1), d =>
        {
            Assert.True(d.IsClosed);
            Assert.Empty(d.Slots);
        });
    }

    [Fact]
    public void WeeklyHoursMarkedClosed_IsClosed()
    {
        var day = Calc(Day, Day, 60, hours: [WeeklyClosed(Day.DayOfWeek)]).Single();

        Assert.True(day.IsClosed);
        Assert.Empty(day.Slots);
    }

    [Fact]
    public void NoWeeklyRowAndNoException_IsClosed()
    {
        var day = Calc(Day, Day, 60, hours: [Weekly(Day.AddDays(1).DayOfWeek, "08:00", "10:00")]).Single();

        Assert.True(day.IsClosed);
        Assert.Empty(day.Slots);
    }

    [Fact]
    public void ClosingException_ClosesANormallyOpenDay()
    {
        var day = Calc(Day, Day, 60,
            hours: [Weekly(Day.DayOfWeek, "08:00", "10:00")],
            exceptions: [ClosedException(Day)]).Single();

        Assert.True(day.IsClosed);
        Assert.Empty(day.Slots);
    }

    [Fact]
    public void OpeningException_OpensADayClosedInTheWeeklySchedule()
    {
        var day = Calc(Day, Day, 60,
            hours: [WeeklyClosed(Day.DayOfWeek)],
            exceptions: [OpenException(Day, "10:00", "12:00")]).Single();

        Assert.False(day.IsClosed);
        Assert.Equal(["16:00", "17:00"], Starts(day));
    }

    [Fact]
    public void OpeningException_OpensADayWithNoWeeklyRow()
    {
        var day = Calc(Day, Day, 60, exceptions: [OpenException(Day, "10:00", "11:00")]).Single();

        Assert.False(day.IsClosed);
        Assert.Equal(["16:00"], Starts(day));
    }

    [Fact]
    public void ExceptionWithDifferentHours_ReplacesTheWeeklyHoursOnlyOnItsDate()
    {
        var nextWeek = Day.AddDays(7);
        var days = Calc(Day, nextWeek, 60,
            hours: [Weekly(Day.DayOfWeek, "08:00", "10:00")],
            exceptions: [OpenException(Day, "13:00", "14:00")]);

        Assert.Equal(["19:00"], Starts(days[0]));
        Assert.Equal(["14:00", "15:00"], Starts(days[7]));
    }

    // ---------- slot generation ----------

    [Fact]
    public void SlotThatWouldEndAfterClosing_IsDropped()
    {
        var day = Calc(Day, Day, 30, hours: [Weekly(Day.DayOfWeek, "08:00", "09:45")]).Single();

        // 09:30–10:00 local would end after 09:45.
        Assert.Equal(["14:00", "14:30", "15:00"], Starts(day));
        Assert.Equal(new DateTime(2026, 9, 1, 15, 30, 0, DateTimeKind.Utc), day.Slots[^1].EndAt);
    }

    [Fact]
    public void SlotLength_SetsBothStepAndDuration()
    {
        var day = Calc(Day, Day, 45, hours: [Weekly(Day.DayOfWeek, "08:00", "10:00")]).Single();

        Assert.Equal(["14:00", "14:45"], Starts(day));
        Assert.All(day.Slots, s => Assert.Equal(TimeSpan.FromMinutes(45), s.EndAt - s.StartAt));
    }

    // ---------- bookings ----------

    // Open 08:00–11:00 local (14:00Z–17:00Z), hourly slots: 14:00, 15:00, 16:00.
    [Theory]
    [InlineData("exact overlap", "15:00", "16:00", new[] { "14:00", "16:00" })]
    [InlineData("partial overlap of two slots", "15:30", "16:30", new[] { "14:00" })]
    [InlineData("booking inside a slot", "15:15", "15:45", new[] { "14:00", "16:00" })]
    [InlineData("back-to-back before opening", "13:00", "14:00", new[] { "14:00", "15:00", "16:00" })]
    [InlineData("back-to-back after closing", "17:00", "18:00", new[] { "14:00", "15:00", "16:00" })]
    [InlineData("ends exactly where the next slot starts", "14:00", "15:00", new[] { "15:00", "16:00" })]
    public void ActiveBooking_RemovesOnlyTheSlotsItOverlaps(string _, string startUtc, string endUtc, string[] expected)
    {
        var day = Calc(Day, Day, 60,
            hours: [Weekly(Day.DayOfWeek, "08:00", "11:00")],
            bookings: [Booked(startUtc, endUtc, BookingStatus.CONFIRMED)]).Single();

        Assert.Equal(expected, Starts(day));
    }

    [Theory]
    [InlineData(BookingStatus.PENDING, new[] { "14:00", "16:00" })]
    [InlineData(BookingStatus.CONFIRMED, new[] { "14:00", "16:00" })]
    [InlineData(BookingStatus.CANCELLED, new[] { "14:00", "15:00", "16:00" })]
    [InlineData(BookingStatus.COMPLETED, new[] { "14:00", "15:00", "16:00" })]
    [InlineData(BookingStatus.NO_SHOW, new[] { "14:00", "15:00", "16:00" })]
    public void OnlyPendingAndConfirmedBookings_BlockSlots(BookingStatus status, string[] expected)
    {
        var day = Calc(Day, Day, 60,
            hours: [Weekly(Day.DayOfWeek, "08:00", "11:00")],
            bookings: [Booked("15:00", "16:00", status)]).Single();

        Assert.Equal(expected, Starts(day));
    }

    [Fact]
    public void FullyBookedDay_IsOpenWithNoSlots()
    {
        var day = Calc(Day, Day, 60,
            hours: [Weekly(Day.DayOfWeek, "08:00", "10:00")],
            bookings: [Booked("14:00", "16:00", BookingStatus.PENDING)]).Single();

        Assert.False(day.IsClosed);
        Assert.Empty(day.Slots);
    }

    // ---------- clock ----------

    [Theory]
    [InlineData("15:10", new[] { "16:00" })]          // mid-slot: the started 15:00 slot is gone
    [InlineData("15:00", new[] { "15:00", "16:00" })] // a slot starting exactly now is kept
    [InlineData("17:00", new string[0])]              // after closing: open, but nothing left
    public void SlotsStartingBeforeNow_AreRemoved(string nowUtc, string[] expected)
    {
        var now = DateTimeOffset.Parse($"2026-09-01T{nowUtc}:00Z", CultureInfo.InvariantCulture);

        var day = Calc(Day, Day, 60, hours: [Weekly(Day.DayOfWeek, "08:00", "11:00")], now: now).Single();

        Assert.False(day.IsClosed);
        Assert.Equal(expected, Starts(day));
    }

    [Fact]
    public void PastSlots_OnlyAffectPastDays()
    {
        var now = new DateTimeOffset(2026, 9, 1, 23, 0, 0, TimeSpan.Zero);

        var days = Calc(Day, Day.AddDays(7), 60, hours: [Weekly(Day.DayOfWeek, "08:00", "10:00")], now: now);

        Assert.Empty(days[0].Slots);
        Assert.Equal(["14:00", "15:00"], Starts(days[7]));
    }

    // ---------- helpers ----------

    private static IReadOnlyList<AvailabilityDay> Calc(
        DateOnly from,
        DateOnly to,
        int slotMinutes,
        BusinessHoursReference[]? hours = null,
        BusinessScheduleExceptionReference[]? exceptions = null,
        BookedInterval[]? bookings = null,
        DateTimeOffset? now = null) =>
        AvailabilityCalculator.Calculate(
            from, to, slotMinutes, hours ?? [], exceptions ?? [], bookings ?? [], now ?? LongAgo, BusinessTimeZone.Instance);

    private static string[] Starts(AvailabilityDay day) =>
        day.Slots.Select(s => s.StartAt.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();

    private static BusinessHoursReference Weekly(DayOfWeek day, string open, string close) => new()
    {
        Id = Guid.NewGuid(),
        DayOfWeek = (short)day,
        OpenTime = TimeOnly.Parse(open, CultureInfo.InvariantCulture),
        CloseTime = TimeOnly.Parse(close, CultureInfo.InvariantCulture),
    };

    private static BusinessHoursReference WeeklyClosed(DayOfWeek day) =>
        new() { Id = Guid.NewGuid(), DayOfWeek = (short)day, IsClosed = true };

    private static BusinessScheduleExceptionReference OpenException(DateOnly date, string open, string close) => new()
    {
        Id = Guid.NewGuid(),
        ExceptionDate = date,
        OpenTime = TimeOnly.Parse(open, CultureInfo.InvariantCulture),
        CloseTime = TimeOnly.Parse(close, CultureInfo.InvariantCulture),
    };

    private static BusinessScheduleExceptionReference ClosedException(DateOnly date) =>
        new() { Id = Guid.NewGuid(), ExceptionDate = date, IsClosed = true };

    /// <summary>A booking on <see cref="Day"/>, with UTC "HH:mm" times.</summary>
    private static BookedInterval Booked(string startUtc, string endUtc, BookingStatus status) => new(
        DateTimeOffset.Parse($"2026-09-01T{startUtc}:00Z", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse($"2026-09-01T{endUtc}:00Z", CultureInfo.InvariantCulture),
        status);
}
