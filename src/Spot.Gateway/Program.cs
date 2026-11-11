using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

// The port only other services can reach (not published in docker-compose.yml). Routes with the
// InternalOnly metadata are served on it and nowhere else (#79).
var internalPort = builder.Configuration.GetValue<int>("Gateway:InternalPort");

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Browser clients (e.g. Expo web on localhost:8081) need CORS; origins come from Cors:AllowedOrigins.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:8081"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Route definitions and downstream cluster addresses live in the "ReverseProxy" section of
// appsettings.json (localhost ports for `dotnet run`) and are overridden by Jwt-style
// double-underscore environment variables in docker-compose.yml (Compose service names on the
// shared internal port 8080). See appsettings.json for the routes themselves.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Brute-force protection for POST /auth/login and /auth/register (#161): the routes that use it
// name "auth-credentials" as their RateLimiterPolicy in appsettings.json. It lives here because
// the Gateway is the only layer that sees the real client IP; behind it, every request reaches
// Auth from the Gateway's own address. The limit and the window come from RateLimiting:Auth.
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
var authWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth-credentials", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = authWindow }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (rejected, cancellationToken) =>
    {
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            rejected.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        // The contract's Error shape, like every service's ApiError (the Gateway doesn't reference Spot.Shared).
        await rejected.HttpContext.Response.WriteAsJsonAsync(new
        {
            code = "TOO_MANY_REQUESTS",
            message = "Demasiados intentos. Esperá un momento y volvé a intentarlo.",
            timestamp = DateTime.UtcNow,
        }, cancellationToken);
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// The internal port is plain HTTP on purpose: under the https launch profile a redirect would send
// Booking's call to the public HTTPS port, where the internal route answers 404.
app.UseWhen(context => context.Connection.LocalPort != internalPort, branch => branch.UseHttpsRedirection());

// Gateway's own liveness — distinct from the per-service /health/<service> passthrough routes
// mapped below via MapReverseProxy, which proxy to each downstream service's own /health.
app.UseCors();

// After CORS, so a browser client can read the 429 and show its message.
app.UseRateLimiter();

app.MapHealthChecks("/health");

app.MapReverseProxy(proxy =>
{
    // A step here rather than a route authorization policy, which would answer 401/403: to anyone
    // outside, an internal route must look like it doesn't exist. LocalPort comes from the
    // connection itself, so unlike the Host header a client can't forge it. Internal routes have
    // Order -1, so on the public port they match first and notifications-route never forwards them.
    proxy.Use((context, next) =>
    {
        var route = context.GetReverseProxyFeature().Route.Config;
        if (route.Metadata?.ContainsKey("InternalOnly") == true && context.Connection.LocalPort != internalPort)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next();
    });

    // Passing a pipeline replaces YARP's default one, so its steps have to be listed.
    proxy.UseSessionAffinity();
    proxy.UseLoadBalancing();
    proxy.UsePassiveHealthChecks();
});

app.Run();
