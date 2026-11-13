using System.Globalization;
using Spot.Business.Api.DTOs;

namespace Spot.Business.Api.Services;

/// <summary>
/// The opening-hours rules shared by the weekly schedule (#60) and schedule exceptions (#61).
/// The HH:mm:ss format was already checked as a 400 by model validation
/// (<see cref="BusinessHourInput.TimePattern"/>); these are the 422 rules on top of it.
/// </summary>
internal static class OpeningHours
{
    /// <summary>
    /// A closed day ignores any times sent and stores them as null. An open day needs both times,
    /// with <paramref name="openTime"/> strictly before <paramref name="closeTime"/>.
    /// </summary>
    /// <exception cref="InvalidBusinessHoursException">MISSING_OPENING_HOURS or INVALID_TIME_RANGE.</exception>
    public static (TimeOnly? OpenTime, TimeOnly? CloseTime) Parse(bool isClosed, string? openTime, string? closeTime)
    {
        if (isClosed)
            return (null, null);

        if (string.IsNullOrEmpty(openTime) || string.IsNullOrEmpty(closeTime))
            throw new InvalidBusinessHoursException(
                InvalidBusinessHoursException.MissingOpeningHours,
                "openTime y closeTime son requeridos cuando el día no está cerrado.");

        var open = TimeOnly.ParseExact(openTime, BusinessHourInput.TimeFormat, CultureInfo.InvariantCulture);
        var close = TimeOnly.ParseExact(closeTime, BusinessHourInput.TimeFormat, CultureInfo.InvariantCulture);

        if (open >= close)
            throw new InvalidBusinessHoursException(
                InvalidBusinessHoursException.InvalidTimeRange, "openTime debe ser anterior a closeTime.");

        return (open, close);
    }
}
