using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.Services;

/// <summary>
/// Which hours a business is open on one local date — shared by availability (#66) and booking
/// creation (#67) so both apply exactly the same rules.
/// </summary>
public static class OpeningHoursResolver
{
    /// <summary>
    /// The opening hours on a date, or null if the business is closed that day.
    /// <paramref name="exception"/> (that date's schedule exception, if any) overrides
    /// <paramref name="weekly"/> (that date's day-of-week business_hours row, if any); with
    /// neither, the business is closed.
    /// </summary>
    public static (TimeOnly Open, TimeOnly Close)? Resolve(
        BusinessHoursReference? weekly, BusinessScheduleExceptionReference? exception)
    {
        if (exception is not null)
            return exception.IsClosed ? null : AsRange(exception.OpenTime, exception.CloseTime);

        if (weekly is not null && !weekly.IsClosed)
            return AsRange(weekly.OpenTime, weekly.CloseTime);

        return null;
    }

    /// <summary>
    /// The business_hours row for <paramref name="date"/>'s day of week, or null. day_of_week uses
    /// .NET's DayOfWeek numbering (0 = Sunday … 6 = Saturday).
    /// </summary>
    public static BusinessHoursReference? WeeklyRowFor(DateOnly date, IEnumerable<BusinessHoursReference> weeklyHours) =>
        weeklyHours.FirstOrDefault(h => h.DayOfWeek == (short)date.DayOfWeek);

    // The tables' CHECK guarantees both times, with open < close, on a row that isn't closed;
    // anything else is treated as closed rather than guessed at.
    private static (TimeOnly Open, TimeOnly Close)? AsRange(TimeOnly? open, TimeOnly? close) =>
        open is { } o && close is { } c && o < c ? (o, c) : null;
}
