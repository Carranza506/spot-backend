using System.Globalization;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>Matches the "BusinessHour" schema in contracts/spot-api.yaml (times as HH:mm:ss, null on a closed day).</summary>
public sealed record BusinessHourDto(
    Guid Id,
    Guid BusinessId,
    int DayOfWeek,
    string? OpenTime,
    string? CloseTime,
    bool IsClosed,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BusinessHourDto FromEntity(BusinessHours hours) => new(
        Id: hours.Id,
        BusinessId: hours.BusinessId,
        DayOfWeek: hours.DayOfWeek,
        OpenTime: hours.OpenTime?.ToString(BusinessHourInput.TimeFormat, CultureInfo.InvariantCulture),
        CloseTime: hours.CloseTime?.ToString(BusinessHourInput.TimeFormat, CultureInfo.InvariantCulture),
        IsClosed: hours.IsClosed,
        CreatedAt: hours.CreatedAt,
        UpdatedAt: hours.UpdatedAt);
}
