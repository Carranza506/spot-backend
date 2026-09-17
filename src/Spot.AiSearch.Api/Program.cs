using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spot.AiSearch.Api.Data;
using Spot.AiSearch.Api.Options;
using Spot.AiSearch.Api.Repositories;
using Spot.AiSearch.Api.Services;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.Shared.Auth;
using Spot.Shared.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // A malformed query value (userId=not-a-guid, status=BOGUS, page=abc) fails model
        // binding before the action runs. Without this, [ApiController] would answer with
        // ASP.NET Core's default ValidationProblemDetails instead of the { code, message,
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

builder.Services.AddDbContext<AiSearchDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_aisearch")));

builder.Services.AddScoped<IAiRequestRepository, AiRequestRepository>();
builder.Services.AddScoped<IAiRequestService, AiRequestService>();

var geminiSection = builder.Configuration.GetSection(GeminiOptions.SectionName);
builder.Services.Configure<GeminiOptions>(geminiSection);
builder.Services.AddHttpClient<IGeminiClient, GeminiClient>();

// Fails fast at boot, same as AddSpotJwtAuthentication, instead of letting every Gemini call
// fail at first use with a 403 that looks like an API problem rather than a config one.
if (string.IsNullOrWhiteSpace(geminiSection.Get<GeminiOptions>()?.ApiKey))
{
    throw new InvalidOperationException(
        $"{GeminiOptions.SectionName}:{nameof(GeminiOptions.ApiKey)} is missing. Set it via user-secrets " +
        "in development or the Gemini__ApiKey environment variable in production (see README.md).");
}

// Shared RS256 JWT validation (signature, issuer, audience, lifetime) configured from the
// "Jwt" section — the same setup every microservice uses. See Spot.Shared.Auth.
builder.Services.AddSpotJwtAuthentication(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
