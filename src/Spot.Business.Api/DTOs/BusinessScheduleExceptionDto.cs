using System.Globalization;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the "BusinessScheduleException" schema in contracts/spot-api.yaml (exceptionDate as
/// YYYY-MM-DD, times as HH:mm:ss, null on a closed day).
/// </summary>
public sealed record BusinessScheduleExceptionDto(
    Guid Id,
    Guid BusinessId,
    DateOnly ExceptionDate,
    bool IsClosed,
    string? OpenTime,
    string? CloseTime,
    string? Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BusinessScheduleExceptionDto FromEntity(BusinessScheduleException exception) => new(
        Id: exception.Id,
        BusinessId: exception.BusinessId,
        ExceptionDate: exception.ExceptionDate,
        IsClosed: exception.IsClosed,
        OpenTime: exception.OpenTime?.ToString(BusinessHourInput.TimeFormat, CultureInfo.InvariantCulture),
        CloseTime: exception.CloseTime?.ToString(BusinessHourInput.TimeFormat, CultureInfo.InvariantCulture),
        Reason: exception.Reason,
        CreatedAt: exception.CreatedAt,
        UpdatedAt: exception.UpdatedAt);
}
