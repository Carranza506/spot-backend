var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Route definitions and downstream cluster addresses live in the "ReverseProxy" section of
// appsettings.json (localhost ports for `dotnet run`) and are overridden by Jwt-style
// double-underscore environment variables in docker-compose.yml (Compose service names on the
// shared internal port 8080). See appsettings.json for the routes themselves.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

// Gateway's own liveness — distinct from the per-service /health/<service> passthrough routes
// mapped below via MapReverseProxy, which proxy to each downstream service's own /health.
app.MapHealthChecks("/health");

app.MapReverseProxy();

app.Run();
