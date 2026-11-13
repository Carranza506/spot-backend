using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Spot.Business.Api.Models;
using Spot.Shared.Validation;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the BusinessContactCreateRequest schema in contracts/spot-api.yaml. <see cref="Value"/>
/// is free text, except for PHONE and WHATSAPP: those must be a valid phone number (see
/// <see cref="PhoneNumberRules"/>), since the apps build tel: and WhatsApp links from them.
/// </summary>
public sealed class BusinessContactCreateRequest : IValidatableObject
{
    /// <summary>Nullable only so [Required] can tell "missing" apart from the enum's first member.</summary>
    [Required]
    [JsonConverter(typeof(ContactTypeJsonConverter))]
    public ContactType? Type { get; set; }

    [Required]
    public string Value { get; set; } = default!;

    /// <summary>Contract default: false. True makes this the business's only primary contact.</summary>
    public bool IsPrimary { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already rejects null/"" — this catches the "   " case it doesn't.
        if (!string.IsNullOrEmpty(Value) && string.IsNullOrWhiteSpace(Value))
            yield return new ValidationResult("value no puede estar vacío ni contener solo espacios.", [nameof(Value)]);
        else if (IsPhone(Type) && !string.IsNullOrEmpty(Value) && PhoneNumberRules.ToE164(Value) is null)
            yield return new ValidationResult(PhoneNumberRules.InvalidMessage("value"), [nameof(Value)]);
    }

    /// <summary>Contact types whose value is a phone number.</summary>
    public static bool IsPhone(ContactType? type) => type is ContactType.PHONE or ContactType.WHATSAPP;
}
