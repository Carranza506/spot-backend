using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
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

// modelBuilder.HasPostgresEnum<T>() (in AuthDbContext) only teaches EF's migrations about the
// native Postgres enum types — it does NOT teach Npgsql's ADO.NET layer how to read/write them.
// Without registering the same enums here via NpgsqlDataSourceBuilder, any INSERT/UPDATE that
// touches a `user_role`/`auth_provider` column throws "Reading and writing unmapped enums
// requires an explicit opt-in" the moment Npgsql tries to read the value back from Postgres.
// The snake-case translator must be passed explicitly — MapEnum<T>()'s own default translator
// does NOT match HasPostgresEnum<T>()'s, so without it Npgsql looks up a PG type named
// "UserRole" (nothing matches) instead of the real lowercase "user_role" the migration created.
var npgsqlDataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection"))
    .MapEnum<UserRole>(nameTranslator: new NpgsqlSnakeCaseNameTranslator())
    .MapEnum<AuthProvider>(nameTranslator: new NpgsqlSnakeCaseNameTranslator())
    .Build();

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(npgsqlDataSource,
        npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory_auth")
            // The NpgsqlDataSourceBuilder.MapEnum<T>() calls above only teach the raw ADO.NET
            // connection how to read/write these enums — EF Core's own compiled materializers
            // (used to read RETURNING values after INSERT/UPDATE) need the SAME enums registered
            // here too, on the EF-specific options builder, or they still read the column as a
            // plain int and blow up with the same "unmapped enums" error.
            .MapEnum<UserRole>(nameTranslator: new NpgsqlSnakeCaseNameTranslator())
            .MapEnum<AuthProvider>(nameTranslator: new NpgsqlSnakeCaseNameTranslator())));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

// Stateless — safe as a singleton, same as JwtTokenService above.
builder.Services.AddSingleton<IRefreshTokenHasher, Sha256RefreshTokenHasher>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
// Revokes (POST /auth/logout) and rotates (POST /auth/refresh) existing refresh tokens — not to
// be confused with IRefreshTokenIssuer below, which mints a token from scratch (POST
// /auth/register, and later /login). RefreshTokenService itself depends on IRefreshTokenIssuer
// to mint the replacement token during a rotation.
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

builder.Services.Configure<RefreshTokenOptions>(builder.Configuration.GetSection(RefreshTokenOptions.SectionName));
builder.Services.AddSingleton<IRefreshTokenIssuer, RefreshTokenIssuer>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();

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
