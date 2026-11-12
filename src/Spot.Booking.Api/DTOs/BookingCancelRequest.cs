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

    /// <summary>The reason to store: trimmed, or null when it's missing or blank.</summary>
    public string? NormalizedReason() => string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NormalizedReason()?.Length > ReasonMaxLength)
            yield return new ValidationResult($"reason no puede superar {ReasonMaxLength} caracteres.", [nameof(Reason)]);
    }
}
