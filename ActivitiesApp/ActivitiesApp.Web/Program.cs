using ActivitiesApp.Web.Components;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Web.Services;
using LocationService = ActivitiesApp.Shared.Services.LocationService;
using ActivitiesApp.Protos;
using Grpc.Net.Client;
using System.Diagnostics;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the ActivitiesApp.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();

// The deployed API exposes a dedicated cleartext HTTP/2 port for native in-cluster gRPC.
var apiAddress = builder.Configuration["Services:activitiesapp-api:https:0"]
    ?? builder.Configuration["ApiAddress"]
    ?? "https://localhost:7051";
var webVersion = builder.Configuration["APP_VERSION"]
    ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "unknown";

builder.Services.AddSingleton(apiAddress); // expose for diagnostics
builder.Services.AddSingleton(new WebDiagnosticInfo(webVersion, apiAddress));
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Connecting to API at {ApiAddress} via native gRPC over HTTP/2", apiAddress);
    var httpClient = new HttpClient(new GrpcLoggingHandler(
        new SocketsHttpHandler(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<GrpcLoggingHandler>()))
    {
        DefaultRequestVersion = new Version(2, 0),
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };
    var channel = GrpcChannel.ForAddress(apiAddress, new GrpcChannelOptions { HttpClient = httpClient });
    return new ActivityService.ActivityServiceClient(channel);
});
builder.Services.AddScoped<IActivityService, ActivityGrpcClient>();
builder.Services.AddSingleton<LocationService>();

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("Web started. Version={Version} ApiAddress={ApiAddress} Environment={Environment}",
    webVersion, apiAddress, app.Environment.EnvironmentName);

app.MapDefaultEndpoints();

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var sw = Stopwatch.StartNew();
    logger.LogInformation("Web request starting: {Method} {Path}", context.Request.Method, context.Request.Path);

    try
    {
        await next();
        logger.LogInformation("Web request completed: {Method} {Path} status={StatusCode} in {DurationMs}ms",
            context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Web request failed: {Method} {Path} after {DurationMs}ms",
            context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
        throw;
    }
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapGet("/diag/version", (WebDiagnosticInfo info, IWebHostEnvironment env) => Results.Ok(new
{
    app = "ActivitiesApp.Web",
    version = info.Version,
    apiAddress = info.ApiAddress,
    environment = env.EnvironmentName,
    machine = Environment.MachineName,
    utc = DateTime.UtcNow
}));

app.MapGet("/diag/api/list", async (IActivityService activityService, ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();
    try
    {
        var items = await activityService.ListActivitiesAsync();
        logger.LogInformation("Web diag list succeeded with {Count} items in {DurationMs}ms", items.Count, sw.ElapsedMilliseconds);
        return Results.Ok(new
        {
            ok = true,
            count = items.Count,
            sample = items.Take(5).Select(a => new { a.Id, a.Name, a.City, a.PlaceId, a.ImageUrl }),
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Web diag list failed after {DurationMs}ms", sw.ElapsedMilliseconds);
        return Results.Problem(
            title: "Web-to-API list diagnostic failed",
            detail: ex.ToString(),
            statusCode: 500);
    }
});

app.MapGet("/diag/api/discover", async (
    double lat,
    double lng,
    int? radiusMeters,
    IActivityService activityService,
    ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();
    var radius = radiusMeters ?? 16093;
    try
    {
        var items = await activityService.DiscoverActivitiesAsync(lat, lng, radius);
        logger.LogInformation(
            "Web diag discover succeeded at ({Lat},{Lng}) radius={Radius} with {Count} items in {DurationMs}ms",
            lat, lng, radius, items.Count, sw.ElapsedMilliseconds);
        return Results.Ok(new
        {
            ok = true,
            lat,
            lng,
            radiusMeters = radius,
            count = items.Count,
            sample = items.Take(5).Select(a => new { a.Id, a.Name, a.City, a.PlaceId, a.ImageUrl }),
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Web diag discover failed at ({Lat},{Lng}) radius={Radius} after {DurationMs}ms",
            lat, lng, radius, sw.ElapsedMilliseconds);
        return Results.Problem(
            title: "Web-to-API discover diagnostic failed",
            detail: ex.ToString(),
            statusCode: 500);
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(ActivitiesApp.Shared._Imports).Assembly);

app.Run();

internal sealed record WebDiagnosticInfo(string Version, string ApiAddress);
