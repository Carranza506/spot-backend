using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Configuration;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;
using Spot.Shared.Auth;
using Spot.Shared.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().ConfigureSpotApiErrorShape();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_auth")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

// Stateless — safe as a singleton, same as JwtTokenService above.
builder.Services.AddSingleton<IRefreshTokenHasher, Sha256RefreshTokenHasher>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
// Revokes an existing refresh token (POST /auth/logout) — not to be confused with
// IRefreshTokenIssuer below, which mints a new one (POST /auth/register, and later /login).
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

builder.Services.Configure<RefreshTokenOptions>(builder.Configuration.GetSection(RefreshTokenOptions.SectionName));
builder.Services.AddSingleton<IRefreshTokenIssuer, RefreshTokenIssuer>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Validates incoming access tokens (public key only) for [Authorize] endpoints such as
// POST /auth/logout — separate from JwtTokenService above, which signs new tokens with the
// private key. Same shared setup every other microservice uses.
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
