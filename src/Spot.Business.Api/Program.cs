using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
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

// Same setup as Spot.Auth.Api's Program.cs (see the full explanation there): HasPostgresEnum<T>()
// in BusinessDbContext only teaches migrations about the native "contact_type" enum, so without
// these MapEnum<T>() registrations any read/write of business_contacts.type fails with
// "Reading and writing unmapped enums requires an explicit opt-in". The snake-case translator
// matches the lowercase labels the migration actually created ("whatsapp", not "WHATSAPP"), and
// is a single shared instance so EF Core doesn't build a new internal service provider per request.
var nameTranslator = new NpgsqlSnakeCaseNameTranslator();
var npgsqlDataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection"))
    .MapEnum<ContactType>(nameTranslator: nameTranslator)
    .Build();

builder.Services.AddDbContext<BusinessDbContext>(options =>
    options.UseNpgsql(npgsqlDataSource,
        npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory_business")
            .MapEnum<ContactType>(nameTranslator: nameTranslator)));

builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IBusinessRepository, BusinessRepository>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<IServiceOfferingRepository, ServiceOfferingRepository>();
builder.Services.AddScoped<IServiceOfferingService, ServiceOfferingService>();
builder.Services.AddScoped<IBusinessContactRepository, BusinessContactRepository>();
builder.Services.AddScoped<IBusinessContactService, BusinessContactService>();
builder.Services.AddScoped<IBusinessScheduleExceptionRepository, BusinessScheduleExceptionRepository>();
builder.Services.AddScoped<IBusinessScheduleExceptionService, BusinessScheduleExceptionService>();

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
