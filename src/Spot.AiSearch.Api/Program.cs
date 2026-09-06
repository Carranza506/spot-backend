using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Spot.AiSearch.Api.Data;
using Spot.AiSearch.Api.Repositories;
using Spot.AiSearch.Api.Services;
using Spot.Shared.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Aplica la misma política a las respuestas escritas fuera de MVC (eventos JWT).
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AiSearchDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IAiRequestRepository, AiRequestRepository>();
builder.Services.AddScoped<IAiRequestService, AiRequestService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("Jwt");
        var publicKeyPem = jwt["PublicKeyPem"];
        var publicKeyPath = jwt["PublicKeyPath"];

        options.MapInboundClaims = false;

        // Spot.Auth.Api firma los JWT con RS256 (clave privada); acá solo se
        // verifica la firma con la clave pública. La privada nunca sale de Auth.
        var tokenValidation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RoleClaimType = "role",
            NameClaimType = "sub",
        };

        if (!string.IsNullOrWhiteSpace(publicKeyPem) || !string.IsNullOrWhiteSpace(publicKeyPath))
        {
            // Clave pública RS256 provista por configuración (PEM literal o archivo).
            var pem = !string.IsNullOrWhiteSpace(publicKeyPem)
                ? publicKeyPem
                : File.ReadAllText(publicKeyPath!);

            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            tokenValidation.IssuerSigningKey = new RsaSecurityKey(rsa);
        }
        else
        {
            // Producción: el Auth service publica su clave pública vía OIDC/JWKS.
            options.Authority = jwt["Authority"];
        }

        options.TokenValidationParameters = tokenValidation;

        // Respuestas de error con el mismo formato del contrato (code, message, timestamp).
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted)
                    return;

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new ApiError("UNAUTHORIZED", "Tu sesión expiró, por favor inicia sesión nuevamente."));
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                    return;

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
