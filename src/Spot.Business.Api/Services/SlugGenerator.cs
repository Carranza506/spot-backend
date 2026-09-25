using System.Globalization;
using System.Text;

namespace Spot.Business.Api.Services;

/// <summary>
/// Builds URL slugs for businesses (businesses.slug, varchar(180), unique). The contract's
/// BusinessCreateRequest has no slug, so it's always derived from the name.
/// </summary>
public static class SlugGenerator
{
    public const int MaxLength = 180;

    /// <summary>Used when a name has no ASCII letters/digits at all (e.g. only symbols), so a slug is never empty.</summary>
    public const string Fallback = "negocio";

    /// <summary>
    /// Lowercases, strips diacritics ("Salón Ñandú" → "salon-nandu"), collapses every run of
    /// anything that isn't [a-z0-9] into a single "-", trims dashes, and caps the length.
    /// </summary>
    public static string FromName(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingDash = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            var lower = char.ToLowerInvariant(c);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');

                builder.Append(lower);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = Truncate(builder.ToString(), MaxLength);
        return slug.Length == 0 ? Fallback : slug;
    }

    /// <summary>
    /// The <paramref name="suffix"/>-th candidate for <paramref name="baseSlug"/>: the base itself
    /// for 1, then "base-2", "base-3"… — truncating the base so the result still fits
    /// <see cref="MaxLength"/>.
    /// </summary>
    public static string WithSuffix(string baseSlug, int suffix)
    {
        if (suffix <= 1)
            return baseSlug;

        var tail = $"-{suffix}";
        return Truncate(baseSlug, MaxLength - tail.Length) + tail;
    }

    private static string Truncate(string slug, int maxLength) =>
        slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
}
