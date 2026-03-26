using ActivitiesApp.Api.Data;
using ActivitiesApp.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddGrpc();

// Determine database provider from environment
var dbProvider = builder.Configuration["DATABASE_PROVIDER"] ?? "Cosmos";

if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("ActivitiesDb");

    // Build connection string from individual env vars if not provided as a single string
    if (string.IsNullOrEmpty(connectionString))
    {
        var host = builder.Configuration["POSTGRES_HOST"] ?? "postgres";
        var db = builder.Configuration["POSTGRES_DB"] ?? "activitiesdb";
        var user = builder.Configuration["POSTGRES_USER"] ?? "activitiesapp";
        var password = builder.Configuration["POSTGRES_PASSWORD"]
            ?? throw new InvalidOperationException("POSTGRES_PASSWORD is required when DATABASE_PROVIDER=Postgres");
        connectionString = $"Host={host};Port=5432;Database={db};Username={user};Password={password}";
    }

    builder.Services.AddDbContext<PostgresDbContext>(options =>
        options.UseNpgsql(connectionString));

    builder.Services.AddScoped<IActivityDbContext>(sp => sp.GetRequiredService<PostgresDbContext>());

    // Register Cosmos context for seeding (read-only source of truth)
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseCosmos(
            accountEndpoint: builder.Configuration["CosmosDb:Endpoint"] ?? "https://localhost:8081/",
            accountKey: builder.Configuration["CosmosDb:Key"] ?? "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            databaseName: "ActivitiesDb"
        ));

    builder.Services.AddScoped<CosmosSeedService>();
}
else
{
    // Default: Cosmos DB (local dev)
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseCosmos(
            accountEndpoint: builder.Configuration["CosmosDb:Endpoint"] ?? "https://localhost:8081/",
            accountKey: builder.Configuration["CosmosDb:Key"] ?? "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            databaseName: "ActivitiesDb"
        ));

    builder.Services.AddScoped<IActivityDbContext>(sp => sp.GetRequiredService<AppDbContext>());
}

// Register Google Places service with HttpClient
builder.Services.AddHttpClient<GooglePlacesService>();

var app = builder.Build();

var startupLog = app.Services.GetRequiredService<ILogger<Program>>();
var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
startupLog.LogInformation("API starting — DbProvider={DbProvider}, Version={Version}, Env={Env}",
    dbProvider, appVersion, app.Environment.EnvironmentName);

// Database initialization
using (var scope = app.Services.CreateScope())
{
    if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        var pgContext = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        await pgContext.Database.EnsureCreatedAsync();
        startupLog.LogInformation("Postgres schema ensured");

        // Seed from Cosmos -> Postgres only if Postgres is empty
        var hasData = await pgContext.Activities.AnyAsync();
        if (!hasData)
        {
            var seedService = scope.ServiceProvider.GetRequiredService<CosmosSeedService>();
            await seedService.SeedAsync();
        }
        else
        {
            startupLog.LogInformation("Postgres already has data — skipping Cosmos seed");
        }
    }
    else
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();
        startupLog.LogInformation("Cosmos DB ensured");
    }
}

app.MapDefaultEndpoints();

// gRPC-Web so HTTP/1.1 callers (Blazor web) can reach gRPC services
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<ActivityGrpcService>().EnableGrpcWeb();
app.MapGet("/", () => $"ActivitiesApp gRPC API is running (v{appVersion}, db={dbProvider}). Use a gRPC client to communicate.");

// Diagnostic endpoints
app.MapGet("/api/version", () => Results.Ok(new
{
    version = appVersion,
    dbProvider,
    environment = app.Environment.EnvironmentName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/api/health/db", async (IServiceProvider sp) =>
{
    try
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IActivityDbContext>();
        var count = await db.Activities.CountAsync();
        return Results.Ok(new { status = "healthy", activityCount = count, dbProvider });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "unhealthy", error = ex.Message, dbProvider },
            statusCode: 503);
    }
});

// Photo proxy — serves Google Places photos without exposing the API key to browsers
app.MapGet("/api/photos", async (string r, int? maxwidth, GooglePlacesService places, HttpContext ctx) =>
{
    var photoRef = r;
    var width = maxwidth ?? 400;

    var imageBytes = await places.FetchPhotoAsync(photoRef, width);
    if (imageBytes is null)
        return Results.NotFound();

    return Results.File(imageBytes, "image/jpeg");
});

app.Run();
