using Spot.Booking.Api.Models;
using Spot.Booking.Api.Services;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Tests.Services;

/// <summary>The status notifications' text (#173): service, business and the local date and time.</summary>
public class BookingNotificationTextTests
{
    // 21:00 UTC is 3:00 p. m. on Friday 13 November 2026 in Costa Rica (UTC-6, no DST).
    private static BookingEntity Booking(BookingStatus status, string? reason = null) => new()
    {
        Status = status,
        ServiceName = "Corte de cabello",
        StartAt = new DateTimeOffset(2026, 11, 13, 21, 0, 0, TimeSpan.Zero),
        CancelReason = reason,
    };

    [Fact]
    public void Confirmed_NamesTheBusiness_TheService_AndTheLocalTime() =>
        Assert.Equal(
            "Barbería La U confirmó tu reservación de Corte de cabello para el viernes 13 de noviembre a las 3:00 p. m.",
            BookingNotificationText.For(Booking(BookingStatus.CONFIRMED), "Barbería La U"));

    [Fact]
    public void Cancelled_IsNeutral_ForBothParties() =>
        Assert.Equal(
            "Se canceló la reservación de Corte de cabello en Barbería La U del viernes 13 de noviembre a las 3:00 p. m.",
            BookingNotificationText.For(Booking(BookingStatus.CANCELLED), "Barbería La U"));

    [Fact]
    public void Cancelled_WithAReason_EndsWithIt() =>
        Assert.Equal(
            "Se canceló la reservación de Corte de cabello en Barbería La U del viernes 13 de noviembre a las 3:00 p. m. Motivo: Me surgió un imprevisto",
            BookingNotificationText.For(Booking(BookingStatus.CANCELLED, "Me surgió un imprevisto"), "Barbería La U"));

    [Fact]
    public void Completed_AndNoShow_SayWhich() =>
        Assert.Equal(
            [
                "Tu reservación de Corte de cabello en Barbería La U del viernes 13 de noviembre a las 3:00 p. m. fue marcada como completada. ¡Gracias por usar Spot!",
                "Tu reservación de Corte de cabello en Barbería La U del viernes 13 de noviembre a las 3:00 p. m. fue marcada como no asistida.",
            ],
            [
                BookingNotificationText.For(Booking(BookingStatus.COMPLETED), "Barbería La U"),
                BookingNotificationText.For(Booking(BookingStatus.NO_SHOW), "Barbería La U"),
            ]);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void WithoutABusinessName_SaysElNegocio(string? name) =>
        Assert.Equal(
            "El negocio confirmó tu reservación de Corte de cabello para el viernes 13 de noviembre a las 3:00 p. m.",
            BookingNotificationText.For(Booking(BookingStatus.CONFIRMED), name));

    [Theory]
    [InlineData(6, 0, "domingo 1 de noviembre a las 12:00 a. m.")] // midnight local
    [InlineData(18, 5, "domingo 1 de noviembre a las 12:05 p. m.")] // noon local
    [InlineData(5, 59, "sábado 31 de octubre a las 11:59 p. m.")] // still the previous local day
    public void TheTime_IsLocal_InTwelveHourFormat(int utcHour, int minute, string expected) =>
        Assert.Contains(
            expected,
            BookingNotificationText.For(
                new BookingEntity
                {
                    Status = BookingStatus.CONFIRMED,
                    ServiceName = "Corte",
                    StartAt = new DateTimeOffset(2026, 11, 1, utcHour, minute, 0, TimeSpan.Zero),
                },
                "X"));

    [Fact]
    public void Pending_HasNoText_SoNotificationsKeepsItsDefault() =>
        Assert.Null(BookingNotificationText.For(Booking(BookingStatus.PENDING), "Barbería La U"));
}
