using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:5023");

builder.Services.AddSingleton<SimulationService>();
builder.Services.AddSingleton<SimulationQueue>();
builder.Services.AddHostedService<SimulationBackgroundService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
    await next();
});

app.UseRouting();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

app.MapGet("/api/version", () =>
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    return Results.Ok(new { version });
});

app.MapPost("/api/simulation/run", (HttpContext context, SimulationRequest? request, SimulationQueue queue) =>
{
    var apiKey = app.Configuration["SimulationApiKey"];
    if (string.IsNullOrEmpty(apiKey))
        return Results.StatusCode(500);

    if (!string.Equals(context.Request.Headers["X-Api-Key"], apiKey, StringComparison.Ordinal))
        return Results.Unauthorized();

    queue.Enqueue(request ?? new SimulationRequest());
    return Results.Accepted();
});

app.MapGet("/api/simulation/latest", async (SimulationService service) =>
{
    var snapshot = await service.GetLatestSnapshotAsync();
    return snapshot is not null ? Results.Ok(snapshot) : Results.NotFound();
});

app.MapGet("/api/simulation/trends", async (SimulationService service) =>
{
    var trendData = await service.GetTrendDataAsync();
    return trendData is not null ? Results.Ok(trendData) : Results.NotFound();
});

app.MapGet("/api/simulation/snapshot", async (DateTime timestamp, SimulationService service) =>
{
    var snapshot = await service.GetSnapshotByTimestampAsync(timestamp);
    return snapshot is not null ? Results.Ok(snapshot) : Results.NotFound();
});

app.MapFallbackToFile("index.html");

app.Run();
