using System.ComponentModel.DataAnnotations;

namespace Spot.Booking.Api.DTOs;

/// <summary>
/// The optional body of cancelBooking in contracts/spot-api.yaml (#163). The reason is validated
/// as it'll be stored, like the other text fields: trimmed, with a blank one meaning none.
/// </summary>
public sealed class BookingCancelRequest : IValidatableObject
{
    /// <summary>bookings.cancel_reason is VARCHAR(255).</summary>
    public const int ReasonMaxLength = 255;

    public string? Reason { get; set; }

    /// <summary>
    /// The reason to store: trimmed, or null when it's missing or blank. NUL becomes a space first,
    /// since Postgres text can't hold it (22021, a 500), as ReviewService and AiSearch do.
    /// </summary>
    public string? NormalizedReason() => Reason?.Replace('\0', ' ').Trim() is { Length: > 0 } reason ? reason : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NormalizedReason()?.Length > ReasonMaxLength)
            yield return new ValidationResult($"reason no puede superar {ReasonMaxLength} caracteres.", [nameof(Reason)]);
    }
}
