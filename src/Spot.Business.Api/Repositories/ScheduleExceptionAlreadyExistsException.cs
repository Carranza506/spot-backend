namespace Spot.Business.Api.Repositories;

/// <summary>Raised when a business already has a schedule exception for that date ((business_id, exception_date) is unique).</summary>
public sealed class ScheduleExceptionAlreadyExistsException(Guid businessId, DateOnly exceptionDate)
    : Exception($"Business '{businessId}' already has a schedule exception on {exceptionDate:yyyy-MM-dd}.")
{
    public Guid BusinessId { get; } = businessId;
    public DateOnly ExceptionDate { get; } = exceptionDate;
}
