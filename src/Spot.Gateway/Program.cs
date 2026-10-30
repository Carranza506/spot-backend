using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

// The port only other services can reach (not published in docker-compose.yml). Routes with the
// InternalOnly metadata are served on it and nowhere else (#79).
var internalPort = builder.Configuration.GetValue<int>("Gateway:InternalPort");

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

// The internal port is plain HTTP on purpose: under the https launch profile a redirect would send
// Booking's call to the public HTTPS port, where the internal route answers 404.
app.UseWhen(context => context.Connection.LocalPort != internalPort, branch => branch.UseHttpsRedirection());

// Gateway's own liveness — distinct from the per-service /health/<service> passthrough routes
// mapped below via MapReverseProxy, which proxy to each downstream service's own /health.
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
