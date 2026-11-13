using PhoneNumbers;

namespace Spot.Shared.Validation;

/// <summary>
/// The one phone rule for every service (BUG-09, #165): a number in international format
/// ("+&lt;country code&gt;&lt;number&gt;", spaces and dashes allowed) that is valid for its country,
/// stored as E.164 ("+50688881234"). libphonenumber is the library the frontend uses too
/// (libphonenumber-js), so both sides accept exactly the same numbers.
/// </summary>
public static class PhoneNumberRules
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>The number as E.164, or null when it's blank or not a valid number for its country.</summary>
    public static string? ToE164(string? value)
    {
        var trimmed = value?.Trim();
        // The country code must be explicit: without it "8888-1234" could belong to any country.
        if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith('+'))
            return null;

        try
        {
            var number = Util.Parse(trimmed, defaultRegion: null);
            return Util.IsValidNumber(number) ? Util.Format(number, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }

    /// <summary>True for a blank value (optional phones) or a valid number.</summary>
    public static bool IsBlankOrValid(string? value) => string.IsNullOrWhiteSpace(value) || ToE164(value) is not null;

    public static string InvalidMessage(string field) =>
        $"{field} debe ser un número de teléfono válido para su país, con el código de país (por ejemplo +506 8888 1234).";
}
