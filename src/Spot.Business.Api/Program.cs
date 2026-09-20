using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Shared.Auth;
using Spot.Shared.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Same convention as every other microservice: a malformed query value or request body
        // fails model binding before the action runs. Without this, [ApiController] would answer
        // with ASP.NET Core's default ValidationProblemDetails instead of the { code, message,
        // timestamp } Error shape defined in contracts/spot-api.yaml.
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

builder.Services.AddDbContext<BusinessDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_business")));

builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

// Shared RS256 JWT validation (signature, issuer, audience, lifetime) configured from the "Jwt"
// section — the same setup every microservice uses. See Spot.Shared.Auth. Needed here even
// though GET /business/categories is public, because creating/updating/deleting a category
// requires the SUPERADMIN role (#55).
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
