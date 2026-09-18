using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Spot.Notifications.Api.Configuration;
using Spot.Notifications.Api.Data;
using Spot.Notifications.Api.Models;
using Spot.Notifications.Api.Repositories;
using Spot.Notifications.Api.Services;
using Spot.Shared.Auth;
using Spot.Shared.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().ConfigureSpotApiErrorShape();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Same reasoning as Spot.Auth.Api's Program.cs: modelBuilder.HasPostgresEnum<T>() only teaches
// EF's migrations about the native Postgres enum type — it does NOT teach Npgsql's ADO.NET layer
// how to read/write it. Without registering it here via NpgsqlDataSourceBuilder, any INSERT/
// UPDATE/SELECT that touches the `platform` column throws "Reading and writing unmapped enums
// requires an explicit opt-in" the moment Npgsql tries to read the value back from Postgres — the
// exact 500 a previous PR hit by skipping this step.
var npgsqlDataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection"))
    .MapEnum<DevicePlatform>(nameTranslator: new NpgsqlSnakeCaseNameTranslator())
    .Build();

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(npgsqlDataSource,
        npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory_notifications")
            // The NpgsqlDataSourceBuilder.MapEnum<T>() call above only teaches the raw ADO.NET
            // connection how to read/write this enum — EF Core's own compiled materializers (used
            // to read RETURNING values after INSERT/UPDATE) need it registered here too, on the
            // EF-specific options builder, or they still read the column as a plain int and blow
            // up with the same "unmapped enums" error.
            .MapEnum<DevicePlatform>(nameTranslator: new NpgsqlSnakeCaseNameTranslator())));

builder.Services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
builder.Services.AddScoped<IDeviceTokenService, DeviceTokenService>();

// Validates incoming access tokens (public key only) for [Authorize] endpoints — every action
// under /notifications/device-tokens requires one. Same shared setup every other microservice
// uses. See Spot.Shared.Auth.
builder.Services.AddSpotJwtAuthentication(builder.Configuration);

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
