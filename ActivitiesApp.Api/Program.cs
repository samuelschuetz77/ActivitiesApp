using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Services;
using ActivitiesApp.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMemoryCache();
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
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly("ActivitiesApp.Infrastructure.Migrations")));

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
        await pgContext.Database.MigrateAsync();
        startupLog.LogInformation("Postgres migrations applied");

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

// ─── REST endpoints for Blazor Web client ───

app.MapGet("/api/activities", async (IActivityDbContext db) =>
{
    var activities = await db.Activities.Where(a => !a.IsDeleted).ToListAsync();
    foreach (var a in activities) FixImageUrl(a);
    return Results.Ok(activities);
});

app.MapGet("/api/activities/{id:guid}", async (Guid id, IActivityDbContext db) =>
{
    var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id);
    if (activity is null) return Results.NotFound();
    FixImageUrl(activity);
    return Results.Ok(activity);
});

app.MapPost("/api/activities", async (ActivitiesApp.Infrastructure.Models.Activity activity, IActivityDbContext db) =>
{
    activity.Id = Guid.NewGuid();
    db.Activities.Add(activity);
    await db.SaveChangesAsync();
    return Results.Created($"/api/activities/{activity.Id}", activity);
});

app.MapGet("/api/discover", async (double lat, double lng, int? radiusMeters,
    IActivityDbContext db, GooglePlacesService places, ILogger<Program> log) =>
{
    var radius = radiusMeters ?? 16093;
    log.LogInformation("REST DiscoverActivities at ({Lat},{Lng}) radius={Radius}m", lat, lng, radius);

    List<GooglePlacesService.NearbyPlace> googlePlaces;
    try
    {
        googlePlaces = await places.SearchNearbyAsync(lat, lng, radius, type: null, keyword: "fun things to do");
        log.LogInformation("REST Discover got {Count} Google places", googlePlaces.Count);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "REST Discover Google search failed, falling back to DB only");
        googlePlaces = [];
    }

    var existingActivities = await db.Activities.Where(a => a.PlaceId != null).ToListAsync();
    var existingByPlaceId = existingActivities
        .Where(a => !string.IsNullOrEmpty(a.PlaceId))
        .GroupBy(a => a.PlaceId!)
        .ToDictionary(g => g.Key, g => g.First());

    var results = new List<ActivitiesApp.Infrastructure.Models.Activity>();
    var newActivities = new List<ActivitiesApp.Infrastructure.Models.Activity>();

    foreach (var place in googlePlaces)
    {
        if (string.IsNullOrEmpty(place.PlaceId)) continue;

        if (existingByPlaceId.TryGetValue(place.PlaceId, out var existing))
        {
            FixImageUrl(existing);
            results.Add(existing);
        }
        else
        {
            var activity = new ActivitiesApp.Infrastructure.Models.Activity
            {
                Id = Guid.NewGuid(),
                Name = place.Name,
                City = place.Vicinity,
                Description = "",
                Cost = place.PriceLevel * 15.0,
                Activitytime = DateTime.UtcNow,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                MinAge = 0,
                MaxAge = 99,
                Category = place.Types.Any(t => t is "park" or "campground") ? "Outdoors"
                    : place.Types.Any(t => t is "restaurant" or "cafe" or "food") ? "Food"
                    : "Social",
                ImageUrl = $"/api/photos/place/{Uri.EscapeDataString(place.PlaceId)}?maxwidth=400",
                PlaceId = place.PlaceId,
                Rating = place.Rating
            };
            newActivities.Add(activity);
            existingByPlaceId[place.PlaceId] = activity;
            results.Add(activity);
        }
    }

    if (newActivities.Count > 0)
    {
        db.Activities.AddRange(newActivities);
        await db.SaveChangesAsync();
    }

    log.LogInformation("REST Discover returning {Count} activities", results.Count);
    return Results.Ok(results);
});

app.MapGet("/api/places/nearby", async (double lat, double lng, int? radiusMeters,
    string? type, string? keyword, GooglePlacesService places) =>
{
    var results = await places.SearchNearbyAsync(lat, lng, radiusMeters ?? 5000, type, keyword);
    return Results.Ok(results);
});

app.MapGet("/api/places/{placeId}", async (string placeId, GooglePlacesService places) =>
{
    var details = await places.GetPlaceDetailsAsync(placeId);
    return details is null ? Results.NotFound() : Results.Ok(details);
});

app.MapGet("/api/geocode/reverse", async (double lat, double lng, GooglePlacesService places) =>
{
    var address = await places.ReverseGeocodeAsync(lat, lng);
    return Results.Ok(new { formattedAddress = address });
});

app.MapGet("/api/geocode/zip/{zipCode}", async (string zipCode, GooglePlacesService places) =>
{
    var result = await places.GeocodePostalCodeAsync(zipCode);
    if (result is null) return Results.NotFound();
    return Results.Ok(new { latitude = result.Value.Latitude, longitude = result.Value.Longitude, formattedAddress = result.Value.FormattedAddress });
});

app.MapGet("/api/geocode/address", async (string address, GooglePlacesService places) =>
{
    var result = await places.GeocodeAddressAsync(address);
    if (result is null) return Results.NotFound();
    return Results.Ok(new { latitude = result.Value.Latitude, longitude = result.Value.Longitude, formattedAddress = result.Value.FormattedAddress });
});

// Legacy photo proxy — serves Google Places photos by raw photo reference
app.MapGet("/api/photos", async (string r, int? maxwidth, GooglePlacesService places, IMemoryCache cache) =>
{
    var width = maxwidth ?? 400;
    var cacheKey = $"photo_ref:{r}:{width}";

    if (cache.TryGetValue(cacheKey, out byte[]? cached) && cached != null)
        return Results.File(cached, "image/jpeg");

    var imageBytes = await places.FetchPhotoAsync(r, width);
    if (imageBytes is null)
        return Results.NotFound();

    cache.Set(cacheKey, imageBytes, TimeSpan.FromHours(24));
    return Results.File(imageBytes, "image/jpeg");
});

// Place-based photo endpoint — uses PlaceId (never expires) to get fresh photos
app.MapGet("/api/photos/place/{placeId}", async (string placeId, int? maxwidth, GooglePlacesService places, IMemoryCache cache) =>
{
    var width = maxwidth ?? 400;
    var cacheKey = $"photo_place:{placeId}:{width}";

    if (cache.TryGetValue(cacheKey, out byte[]? cached) && cached != null)
        return Results.File(cached, "image/jpeg");

    // Get fresh photo reference from Google Place Details
    var details = await places.GetPlaceDetailsAsync(placeId);
    var photoUrl = details?.PhotoUrls?.FirstOrDefault();
    if (photoUrl is null)
        return Results.NotFound();

    // photoUrl is "/api/photos?r={ref}&maxwidth=800" — extract the reference
    var uri = new Uri(photoUrl, UriKind.Relative);
    var query = System.Web.HttpUtility.ParseQueryString(photoUrl.Split('?').LastOrDefault() ?? "");
    var photoRef = query["r"];
    if (string.IsNullOrEmpty(photoRef))
        return Results.NotFound();

    var imageBytes = await places.FetchPhotoAsync(photoRef, width);
    if (imageBytes is null)
        return Results.NotFound();

    cache.Set(cacheKey, imageBytes, TimeSpan.FromHours(24));
    return Results.File(imageBytes, "image/jpeg");
});

// Ensures ImageUrl uses the place-based endpoint (never expires) when a PlaceId is available
void FixImageUrl(ActivitiesApp.Infrastructure.Models.Activity activity)
{
    // If we have a PlaceId, always use the place-based endpoint
    if (!string.IsNullOrEmpty(activity.PlaceId))
    {
        activity.ImageUrl = $"/api/photos/place/{Uri.EscapeDataString(activity.PlaceId)}?maxwidth=400";
        return;
    }
    if (string.IsNullOrEmpty(activity.ImageUrl)) return;
    // Already a valid URL
    if (activity.ImageUrl.StartsWith("/api/photos") || activity.ImageUrl.StartsWith("http")) return;
    // Raw Google photo reference — convert to proxy URL (legacy fallback)
    activity.ImageUrl = $"/api/photos?r={Uri.EscapeDataString(activity.ImageUrl)}&maxwidth=400";
}

app.Run();
