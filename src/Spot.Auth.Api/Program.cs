using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Configuration;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;
using Spot.Shared.Auth;
using Spot.Shared.Errors;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Replaces ASP.NET Core's default ValidationProblemDetails (a different shape entirely)
        // with the contract's Error schema, so a malformed request body/query — caught by model
        // binding/validation before any action runs — still gets a 400 the client can parse the
        // same way as every other error response.
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? "La solicitud no es válida.";

            return new BadRequestObjectResult(new ApiError("BAD_REQUEST", message));
        };
    });

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services.Configure<RefreshTokenOptions>(builder.Configuration.GetSection(RefreshTokenOptions.SectionName));
builder.Services.AddSingleton<IRefreshTokenService, RefreshTokenService>();

// PBKDF2 (HMAC-SHA256, 100k+ iterations, random per-hash salt) via ASP.NET Core's own,
// already-audited implementation — never hand-roll password hashing.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Catches anything an action/service didn't handle itself (e.g. the database being
// unreachable) and still answers with the contract's Error shape instead of ASP.NET Core's
// default — and, per contract, never a stack trace or other internal detail.
app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(
        new ApiError("INTERNAL_ERROR", "Ocurrió un error inesperado. Por favor intenta de nuevo más tarde."));
}));

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
