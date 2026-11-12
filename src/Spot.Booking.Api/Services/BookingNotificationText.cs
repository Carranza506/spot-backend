using Spot.Booking.Api.Models;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Services;

/// <summary>
/// The body of a status notification (#173): which service, at which business and when, so a client
/// with several bookings (or a business with many) can tell which one changed. Sent as the
/// notification's message; Spot.Notifications.Api keeps its own titles.
/// </summary>
public static class BookingNotificationText
{
    // Spelled out instead of a "es-CR" CultureInfo: the text must not depend on the container's ICU data.
    private static readonly string[] Days = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] Months =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    /// <summary>Null for a status that isn't notified, so Notifications keeps its default text.</summary>
    public static string? For(BookingEntity booking, string? businessName)
    {
        var local = TimeZoneInfo.ConvertTime(booking.StartAt, BusinessTimeZone.Instance);
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        var when = $"{Days[(int)local.DayOfWeek]} {local.Day} de {Months[local.Month - 1]} a las {hour}:{local.Minute:00} {(local.Hour < 12 ? "a. m." : "p. m.")}";
        var business = string.IsNullOrWhiteSpace(businessName) ? "el negocio" : businessName.Trim();
        var service = booking.ServiceName;

        // {when} ends with "a. m."/"p. m.": its period closes a sentence that ends there.
        return booking.Status switch
        {
            BookingStatus.CONFIRMED => $"{char.ToUpperInvariant(business[0])}{business[1..]} confirmó tu reservación de {service} para el {when}",
            // Neutral on purpose: it goes to both the client and the business account.
            BookingStatus.CANCELLED => $"Se canceló la reservación de {service} en {business} del {when}"
                                       + (booking.CancelReason is { } reason ? $" Motivo: {reason}" : ""),
            BookingStatus.COMPLETED => $"Tu reservación de {service} en {business} del {when} fue marcada como completada. ¡Gracias por usar Spot!",
            BookingStatus.NO_SHOW => $"Tu reservación de {service} en {business} del {when} fue marcada como no asistida.",
            _ => null,
        };
    }
}
