using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Spot.Booking.Api.Auth;
using Spot.Booking.Api.Data;
using Spot.Shared.Auth;
using Spot.Booking.Api.Models;
using Spot.Booking.Api.Repositories;
using Spot.Booking.Api.Services;
using Spot.Shared.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Same convention as every other microservice: a malformed query/route value fails model
        // binding before the action runs, and is answered with the { code, message, timestamp }
        // Error shape from contracts/spot-api.yaml instead of ASP.NET Core's ValidationProblemDetails.
        options.InvalidModelStateResponseFactory = context =>
        {
            var invalidField = context.ModelState
                .FirstOrDefault(entry => entry.Value?.Errors.Count > 0).Key;

            var error = new ApiError(
                "BAD_REQUEST",
                "Uno o más parámetros de la petición no son válidos.",
                string.IsNullOrEmpty(invalidField) ? null : new { field = invalidField });

            return new BadRequestObjectResult(error);
        };
    });

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Same setup as Spot.Business.Api's Program.cs: HasPostgresEnum<BookingStatus>() in BookingDbContext
// only teaches migrations about the native "booking_status" enum; without these MapEnum<T>()
// registrations any query on bookings.status fails with "Reading and writing unmapped enums
// requires an explicit opt-in". The snake-case translator matches the lowercase labels the
// InitialCreate migration actually created ("pending", "confirmed", …, not db/Spot.sql's
// uppercase ones), and is a single shared instance so EF Core doesn't build a new internal
// service provider per request.
var nameTranslator = new NpgsqlSnakeCaseNameTranslator();
var npgsqlDataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection"))
    .MapEnum<BookingStatus>(nameTranslator: nameTranslator)
    .Build();

builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(npgsqlDataSource,
        npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory_booking")
            .MapEnum<BookingStatus>(nameTranslator: nameTranslator)));

// Injected so "slots in the past are excluded" (#66) and "no bookings in the past" (#67) can be
// tested with a fixed clock.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IBookingService, BookingService>();

// Shared RS256 JWT validation (signature, issuer, audience, lifetime) configured from the "Jwt"
// section — the same setup every microservice uses. See Spot.Shared.Auth. Needed since #67:
// creating a booking requires the CLIENT role. Availability (#66) stays public (no [Authorize]).
builder.Services.AddSpotJwtAuthentication(builder.Configuration);
// 403s (valid token, wrong role) in the Error shape too; AddSpotJwtAuthentication only covers 401.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenErrorResultHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(
        new ApiError("INTERNAL_ERROR", "Ocurrió un error inesperado. Por favor intenta de nuevo más tarde."));
}));

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
