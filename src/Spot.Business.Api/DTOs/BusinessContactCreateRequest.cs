using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Matches the BusinessContactCreateRequest schema in contracts/spot-api.yaml. <see cref="Value"/>
/// is free text on purpose: the contract only types it as a string, so there's no per-type format
/// check (a WEBSITE value isn't validated as a URL, etc.).
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
    }
}
