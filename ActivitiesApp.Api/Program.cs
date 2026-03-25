using ActivitiesApp.Api.Data;
using ActivitiesApp.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(80, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddGrpc();

// Choose database provider based on environment variable
var dbProvider = builder.Configuration["DATABASE_PROVIDER"] ?? "Cosmos";
var apiVersion = builder.Configuration["APP_VERSION"]
    ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "unknown";

if (dbProvider == "Postgres")
{
    var connectionString = builder.Configuration.GetConnectionString("ActivitiesDb")
        ?? throw new InvalidOperationException("ConnectionStrings:ActivitiesDb is required when DATABASE_PROVIDER=Postgres");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseCosmos(
            accountEndpoint: builder.Configuration["CosmosDb:Endpoint"] ?? "https://localhost:8081/",
            accountKey: builder.Configuration["CosmosDb:Key"] ?? "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            databaseName: "ActivitiesDb"
        ));
}

// Register Google Places service with HttpClient
builder.Services.AddHttpClient<GooglePlacesService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.EnsureCreatedAsync();
}

app.MapDefaultEndpoints();

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var contentType = context.Request.ContentType ?? "(none)";

    logger.LogInformation("HTTP request starting: {Method} {Path} content-type={ContentType}",
        context.Request.Method, context.Request.Path, contentType);

    try
    {
        await next();
        logger.LogInformation("HTTP request completed: {Method} {Path} status={StatusCode} in {DurationMs}ms",
            context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "HTTP request failed: {Method} {Path} after {DurationMs}ms",
            context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
        throw;
    }
});

app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<ActivityGrpcService>().EnableGrpcWeb();
app.MapGet("/", () => "ActivitiesApp gRPC API is running. Use a gRPC client to communicate.");
app.MapGet("/diag/version", () => Results.Ok(new
{
    app = "ActivitiesApp.Api",
    version = apiVersion,
    dbProvider,
    environment = app.Environment.EnvironmentName,
    machine = Environment.MachineName,
    utc = DateTime.UtcNow
}));
app.MapGet("/diag/transport", () => Results.Ok(new
{
    mode = "grpc-web",
    port = 80,
    protocols = "http/1.1 + grpc-web"
}));

app.MapGet("/diag/db", async (AppDbContext db, ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();
    try
    {
        var total = await db.Activities.AsNoTracking().CountAsync();
        var active = await db.Activities.AsNoTracking().CountAsync(a => !a.IsDeleted);
        var googleBacked = await db.Activities.AsNoTracking().CountAsync(a => a.PlaceId != null);
        var sample = await db.Activities.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.UpdatedAt)
            .Take(5)
            .Select(a => new { a.Id, a.Name, a.City, a.PlaceId, a.ImageUrl, a.UpdatedAt })
            .ToListAsync();

        logger.LogInformation("API diag db succeeded in {DurationMs}ms with total={Total} active={Active} googleBacked={GoogleBacked}",
            sw.ElapsedMilliseconds, total, active, googleBacked);

        return Results.Ok(new
        {
            ok = true,
            dbProvider,
            total,
            active,
            googleBacked,
            sample,
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "API diag db failed after {DurationMs}ms", sw.ElapsedMilliseconds);
        return Results.Problem(
            title: "API DB diagnostic failed",
            detail: ex.ToString(),
            statusCode: 500);
    }
});

app.MapGet("/diag/google-nearby", async (
    double lat,
    double lng,
    int? radiusMeters,
    GooglePlacesService places,
    ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();
    var radius = radiusMeters ?? 16093;
    try
    {
        var results = await places.SearchNearbyAsync(lat, lng, radius, null, "fun things to do");
        logger.LogInformation(
            "API diag google-nearby succeeded at ({Lat},{Lng}) radius={Radius} with {Count} places in {DurationMs}ms",
            lat, lng, radius, results.Count, sw.ElapsedMilliseconds);
        return Results.Ok(new
        {
            ok = true,
            lat,
            lng,
            radiusMeters = radius,
            count = results.Count,
            sample = results.Take(5).Select(p => new { p.PlaceId, p.Name, p.Vicinity, p.PhotoUrl, p.Types }),
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "API diag google-nearby failed at ({Lat},{Lng}) radius={Radius} after {DurationMs}ms",
            lat, lng, radius, sw.ElapsedMilliseconds);
        return Results.Problem(
            title: "API Google nearby diagnostic failed",
            detail: ex.ToString(),
            statusCode: 500);
    }
});

startupLogger.LogInformation("API started. Version={Version} DB provider={DbProvider} ApiAddress bound on port 80. Environment={Environment}",
    apiVersion, dbProvider, app.Environment.EnvironmentName);

app.Run();
