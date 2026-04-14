using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Models;
using ActivitiesApp.Infrastructure.Services;
using ActivitiesApp.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Web;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Security.Claims;
using ActivitiesApp.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
var appMeter = new Meter(builder.Environment.ApplicationName);
var activitiesCreatedCounter = appMeter.CreateCounter<long>(
    "activities_created_total",
    unit: "{activity}",
    description: "Number of activities created through the API");

builder.AddServiceDefaults();

builder.Services.AddMemoryCache();
builder.Services.AddGrpc();

// Determine database provider from environment
var dbProvider = builder.Configuration["DATABASE_PROVIDER"] ?? "Cosmos";
var defaultPostgresHost = builder.Environment.IsDevelopment() ? "localhost" : "postgres";
var runMigrationsOnStartup =
    string.Equals(builder.Configuration["RUN_DB_MIGRATIONS_ON_STARTUP"], "true", StringComparison.OrdinalIgnoreCase);

if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("ActivitiesDb");

    // Build connection string from individual env vars if not provided as a single string
    if (string.IsNullOrEmpty(connectionString))
    {
        var host = builder.Configuration["POSTGRES_HOST"] ?? defaultPostgresHost;
        var db = builder.Configuration["POSTGRES_DB"] ?? "activitiesdb";
        var user = builder.Configuration["POSTGRES_USER"] ?? "activitiesapp";
        var password = builder.Configuration["POSTGRES_PASSWORD"] ?? "activitiesapp";
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

// Identity DbContext removed — auth is now handled by Microsoft Entra ID

// ─── Microsoft Entra ID Authentication ───
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");
builder.Services.AddAuthorization();

// Register Google Places service with HttpClient
builder.Services.AddHttpClient<GooglePlacesService>();

var app = builder.Build();

var startupLog = app.Services.GetRequiredService<ILogger<Program>>();
var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
startupLog.LogInformation("Automatic DB migrations on startup: {Enabled}", runMigrationsOnStartup);
startupLog.LogInformation("API starting — DbProvider={DbProvider}, Version={Version}, Env={Env}",
    dbProvider, appVersion, app.Environment.EnvironmentName);

// Database initialization
using (var scope = app.Services.CreateScope())
{
    if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        var pgContext = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        if (runMigrationsOnStartup)
        {
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
            startupLog.LogInformation(
                "Skipping Postgres migrations and seed on startup. Set RUN_DB_MIGRATIONS_ON_STARTUP=true to enable them.");
        }
    }
    else
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();
        startupLog.LogInformation("Cosmos DB ensured");
    }

    // Identity DB migrations removed — auth is now handled by Microsoft Entra ID
}

app.MapDefaultEndpoints();

// gRPC-Web so HTTP/1.1 callers (Blazor web) can reach gRPC services
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.UseAuthentication();
app.UseAuthorization();

// Add cache headers to photo endpoints so browsers/MAUI HTTP clients cache images
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/photos"))
    {
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode == 200)
            {
                context.Response.Headers.CacheControl = "public, max-age=86400, immutable";
            }
            return Task.CompletedTask;
        });
    }
    await next();
});

app.MapGrpcService<ActivityGrpcService>().EnableGrpcWeb();

// Temporary: emit test warning/error so Grafana "Error Count and Warnings" panel has data
app.Logger.LogWarning("Test warning for observability");
app.Logger.LogError("Test error for observability");



app.MapDelete("/api/admin/purge-google-places", async (IActivityDbContext db, ILogger<Program> log) =>
{
    var googleActivities = await db.Activities.Where(a => a.PlaceId != null && a.PlaceId != "").ToListAsync();
    var count = googleActivities.Count;
    db.Activities.RemoveRange(googleActivities);
    await db.SaveChangesAsync();
    log.LogInformation("Purged {Count} Google-sourced activities", count);
    return Results.Ok(new { deleted = count });
});


app.MapGet("/", () => $"ActivitiesApp gRPC API is running (v{appVersion}, db={dbProvider}). Use a gRPC client to communicate.");

// Diagnostic endpoints
app.MapGet("/api/version", () => Results.Ok(new
{
    version = appVersion,
    dbProvider,
    environment = app.Environment.EnvironmentName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/api/quota/status", async (IActivityDbContext db) =>
{
    var status = await GooglePlacesService.GetQuotaStatusAsync(db);
    return Results.Ok(new
    {
        nearbySearch = status["nearby_search"],
        placeDetails = status["place_details"],
        photos = status["photo"],
        geocoding = status["geocode"],
        resetTime = DateTime.UtcNow.Date.AddDays(1)
    });
});

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

// ─── Auth endpoints ───
app.MapAuthEndpoints();

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

app.MapPost("/api/activities", async (ActivitiesApp.Infrastructure.Models.Activity activity, IActivityDbContext db, HttpContext httpContext, ILogger<Program> log) =>
{
    var validationErrors = ValidateActivityForCreate(activity);
    if (validationErrors.Count > 0)
    {
        var userIdForRejectedRequest = httpContext.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
        ?? httpContext.User.FindFirstValue("oid");
        log.LogWarning(
            "REST CreateActivity rejected invalid payload from UserId={UserId}. Errors={ValidationErrors}. Name={Name}, City={City}, Category={Category}",
            userIdForRejectedRequest ?? "anonymous",
            string.Join(" | ", validationErrors),
            activity.Name ?? "",
            activity.City ?? "",
            activity.Category ?? "");
        return Results.ValidationProblem(validationErrors
            .Select((message, index) => new KeyValuePair<string, string[]>($"activity_{index}", [message]))
            .ToDictionary());
    }

    activity.Id = Guid.NewGuid();
    var userId = httpContext.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
        ?? httpContext.User.FindFirstValue("oid");
    activity.CreatedByUserId = userId;
    try
    {
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        log.LogInformation(
            "REST CreateActivity created ActivityId={ActivityId} for UserId={UserId}, Name={Name}, City={City}",
            activity.Id, userId ?? "anonymous", activity.Name, activity.City);
        activitiesCreatedCounter.Add(1,
            new KeyValuePair<string, object?>("creation_source", "manual"));
        return Results.Created($"/api/activities/{activity.Id}", activity);
    }
    catch (Exception ex)
    {
        log.LogError(ex,
            "REST CreateActivity failed for UserId={UserId}, Name={Name}, City={City}",
            userId ?? "anonymous", activity.Name ?? "", activity.City ?? "");
        return Results.Problem("Failed to create activity.");
    }
}).RequireAuthorization();

app.MapGet("/api/discover", async (double lat, double lng, int? radiusMeters, string? tagName,
    IActivityDbContext db, GooglePlacesService places, IMemoryCache discoverCache, ILogger<Program> log) =>
{
    var radius = radiusMeters ?? 16093;
    var discoverRequestId = Guid.NewGuid().ToString("N")[..8];
    log.LogInformation("REST Discover {RequestId} started at ({Lat},{Lng}) radius={Radius}m tag={Tag}", discoverRequestId, lat, lng, radius, tagName ?? "");

    // Server-side cache: quantize to ~1.1km grid to avoid redundant Google calls
    var gridLat = Math.Round(lat, 2);
    var gridLng = Math.Round(lng, 2);
    var discoverCacheKey = $"discover:{gridLat}:{gridLng}:{radius}";

    List<GooglePlacesService.NearbyPlace> googlePlaces;
    try
    {
        // Check server-side cache first
        if (discoverCache.TryGetValue(discoverCacheKey, out List<GooglePlacesService.NearbyPlace>? cachedPlaces) && cachedPlaces != null)
        {
            googlePlaces = cachedPlaces;
            log.LogInformation("REST Discover {RequestId} cache HIT: {Count} places from grid ({GridLat},{GridLng})",
                discoverRequestId, googlePlaces.Count, gridLat, gridLng);
        }
        else
        {
            // Single broad search (no type filter) to minimize API calls
            googlePlaces = await places.SearchNearbyAsync(lat, lng, radius, type: null, keyword: null);
            discoverCache.Set(discoverCacheKey, googlePlaces, TimeSpan.FromMinutes(60));
            log.LogInformation("REST Discover {RequestId} cache MISS: broad search returned {Count} places, cached for 60min",
                discoverRequestId, googlePlaces.Count);
        }

        // If a tag was requested and fewer than 5 results match, do 1 targeted search
        if (!string.IsNullOrWhiteSpace(tagName) &&
            GooglePlaceTagMapper.TryGetDefinition(tagName, out var tagDef) && tagDef != null)
        {
            var matchCount = googlePlaces.Count(p => GooglePlaceTagMapper.GetTags(p.Types)
                .Contains(tagName, StringComparer.OrdinalIgnoreCase));
            if (matchCount < 5)
            {
                log.LogInformation("REST Discover {RequestId} only {MatchCount} matches for tag {Tag}, doing targeted search with type={Type}",
                    discoverRequestId, matchCount, tagName, tagDef.PrimarySearchType);
                var targeted = await places.SearchNearbyAsync(lat, lng, radius, type: tagDef.PrimarySearchType, keyword: null);
                // Merge and deduplicate by PlaceId
                var deduped = googlePlaces.ToDictionary(p => p.PlaceId, p => p, StringComparer.Ordinal);
                foreach (var p in targeted)
                {
                    if (!string.IsNullOrWhiteSpace(p.PlaceId))
                        deduped.TryAdd(p.PlaceId, p);
                }
                googlePlaces = deduped.Values.ToList();
                log.LogInformation("REST Discover {RequestId} after targeted merge: {Count} places", discoverRequestId, googlePlaces.Count);
            }
        }
    }
    catch (Exception ex)
    {
        log.LogError(ex, "REST Discover {RequestId} Google search failed for tag {Tag}, falling back to DB only", discoverRequestId, tagName ?? "");
        googlePlaces = [];
    }

    var existingActivities = await db.Activities.Where(a => a.PlaceId != null).ToListAsync();
    var existingByPlaceId = existingActivities
        .Where(a => !string.IsNullOrEmpty(a.PlaceId))
        .GroupBy(a => a.PlaceId!)
        .ToDictionary(g => g.Key, g => g.First());

    var results = new List<ActivitiesApp.Infrastructure.Models.Activity>();
    var newActivities = new List<ActivitiesApp.Infrastructure.Models.Activity>();
    var updatedExistingCount = 0;

    foreach (var place in googlePlaces)
    {
        if (string.IsNullOrEmpty(place.PlaceId)) continue;

        var tags = GooglePlaceTagMapper.GetTags(place.Types);
        if (tags.Count == 0) continue;

        var category = string.Join(",", tags);

        if (existingByPlaceId.TryGetValue(place.PlaceId, out var existing))
        {
            var wasUpdated = ApplyGooglePlaceData(existing, place, category);
            if (wasUpdated)
            {
                updatedExistingCount++;
            }
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
                Category = category,
                ImageUrl = GetPreferredPlaceImageUrl(place),
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
    }

    if (newActivities.Count > 0 || updatedExistingCount > 0)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            log.LogWarning(
                ex,
                "REST Discover {RequestId} hit a concurrency conflict while saving {NewCount} new and {UpdatedCount} updated rows. Returning current results anyway.",
                discoverRequestId,
                newActivities.Count,
                updatedExistingCount);
        }
    }

    // Sort by distance (closest first) and cap at 30 results
    results = results
        .OrderBy(a => HaversineMeters(lat, lng, a.Latitude, a.Longitude))
        .Take(30)
        .ToList();

    var fastCount = results.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl) && a.ImageUrl.StartsWith("/api/photos?r="));
    var slowCount = results.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl) && a.ImageUrl.StartsWith("/api/photos/place/"));
    var noneCount = results.Count(a => string.IsNullOrWhiteSpace(a.ImageUrl));

    log.LogInformation(
        "REST Discover {RequestId} returning {Count} activities (capped at 30, sorted by distance) with {NewCount} new and {UpdatedCount} updated existing rows, withImages={ImageCount}, imagePaths: fast={Fast}, slow={Slow}, none={None}",
        discoverRequestId, results.Count, newActivities.Count, updatedExistingCount,
        results.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)), fastCount, slowCount, noneCount);

    // Photo pre-warm removed to reduce Google API costs — photos load lazily with 24h cache

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
app.MapGet("/api/photos", async (string r, int? maxwidth, GooglePlacesService places, IMemoryCache cache, ILogger<Program> log) =>
{
    var width = maxwidth ?? 400;
    var cacheKey = $"photo_ref:{r}:{width}";

    if (cache.TryGetValue(cacheKey, out byte[]? cached) && cached != null)
    {
        log.LogDebug("Photo proxy cache hit: ref={PhotoRef}, width={Width}, bytes={ByteCount}", r, width, cached.Length);
        return Results.File(cached, "image/jpeg", enableRangeProcessing: false, lastModified: null,
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{cacheKey.GetHashCode():x}\""));
    }

    log.LogInformation("Photo proxy cache miss: ref={PhotoRef}, width={Width}", r, width);

    var imageBytes = await places.FetchPhotoAsync(r, width);
    if (imageBytes is null)
    {
        log.LogWarning("Photo proxy fetch failed: ref={PhotoRef}, width={Width}", r, width);
        return Results.NotFound();
    }

    cache.Set(cacheKey, imageBytes, TimeSpan.FromHours(24));
    log.LogInformation("Photo proxy fetched: ref={PhotoRef}, width={Width}, bytes={ByteCount}", r, width, imageBytes.Length);
    return Results.File(imageBytes, "image/jpeg", enableRangeProcessing: false, lastModified: null,
        entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{cacheKey.GetHashCode():x}\""));
});

// Place-based photo endpoint — uses PlaceId (never expires) to get fresh photos
app.MapGet("/api/photos/place/{placeId}", async (string placeId, int? maxwidth, GooglePlacesService places, IMemoryCache cache, ILogger<Program> log) =>
{
    var width = maxwidth ?? 400;
    var cacheKey = $"photo_place:{placeId}:{width}";

    if (cache.TryGetValue(cacheKey, out byte[]? cached) && cached != null)
    {
        log.LogDebug("Photo place cache hit: placeId={PlaceId}, width={Width}", placeId, width);
        return Results.File(cached, "image/jpeg", enableRangeProcessing: false, lastModified: null,
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{cacheKey.GetHashCode():x}\""));
    }

    log.LogInformation("Photo place cache miss: placeId={PlaceId}, width={Width}", placeId, width);

    // Cache the photo reference separately to avoid calling Place Details on every request
    var photoRefCacheKey = $"photoref:{placeId}";
    string? photoRef;
    if (cache.TryGetValue(photoRefCacheKey, out string? cachedRef) && cachedRef != null)
    {
        photoRef = cachedRef;
        log.LogDebug("Photo ref cache hit: placeId={PlaceId}", placeId);
    }
    else
    {
        var details = await places.GetPlaceDetailsAsync(placeId);
        var photoUrl = details?.PhotoUrls?.FirstOrDefault();
        if (photoUrl is null)
        {
            log.LogWarning("Photo place no photos: placeId={PlaceId}", placeId);
            return Results.NotFound();
        }

        var query = System.Web.HttpUtility.ParseQueryString(photoUrl.Split('?').LastOrDefault() ?? "");
        photoRef = query["r"];
        if (!string.IsNullOrEmpty(photoRef))
        {
            cache.Set(photoRefCacheKey, photoRef, TimeSpan.FromHours(24));
        }
    }

    if (string.IsNullOrEmpty(photoRef))
        return Results.NotFound();

    var imageBytes = await places.FetchPhotoAsync(photoRef, width);
    if (imageBytes is null)
        return Results.NotFound();

    cache.Set(cacheKey, imageBytes, TimeSpan.FromHours(24));
    log.LogInformation("Photo place fetched: placeId={PlaceId}, width={Width}, bytes={ByteCount}", placeId, width, imageBytes.Length);
    return Results.File(imageBytes, "image/jpeg", enableRangeProcessing: false, lastModified: null,
        entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{cacheKey.GetHashCode():x}\""));
});

// Ensures ImageUrl uses the fast photo proxy (never calls Place Details)
void FixImageUrl(ActivitiesApp.Infrastructure.Models.Activity activity)
{
    if (string.IsNullOrEmpty(activity.ImageUrl)) return;
    // Already a valid proxy or external URL — leave it alone
    if (activity.ImageUrl.StartsWith("/api/photos") || activity.ImageUrl.StartsWith("http")) return;
    // Raw Google photo reference — convert to fast proxy URL (no Place Details call needed)
    activity.ImageUrl = $"/api/photos?r={Uri.EscapeDataString(activity.ImageUrl)}&maxwidth=400";
}

static bool ApplyGooglePlaceData(
    ActivitiesApp.Infrastructure.Models.Activity activity,
    GooglePlacesService.NearbyPlace place,
    string category)
{
    var desiredImageUrl = GetPreferredPlaceImageUrl(place);
    var changed = false;

    if (!string.Equals(activity.Category, category, StringComparison.Ordinal))
    {
        activity.Category = category;
        changed = true;
    }

    if (activity.Rating != place.Rating)
    {
        activity.Rating = place.Rating;
        changed = true;
    }

    if (activity.Latitude != place.Latitude)
    {
        activity.Latitude = place.Latitude;
        changed = true;
    }

    if (activity.Longitude != place.Longitude)
    {
        activity.Longitude = place.Longitude;
        changed = true;
    }

    if (!string.Equals(activity.ImageUrl, desiredImageUrl, StringComparison.Ordinal))
    {
        activity.ImageUrl = desiredImageUrl;
        changed = true;
    }

    return changed;
}

static string GetPreferredPlaceImageUrl(GooglePlacesService.NearbyPlace place)
{
    // Only use the fast photo reference URL — never fall back to /api/photos/place/
    // which calls Place Details ($0.017 per call)
    if (!string.IsNullOrWhiteSpace(place.PhotoUrl))
    {
        return place.PhotoUrl;
    }

    // No photo available from Nearby Search — return empty rather than triggering Place Details
    return "";
}

static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
{
    const double R = 6371000;
    var dLat = (lat2 - lat1) * Math.PI / 180;
    var dLng = (lng2 - lng1) * Math.PI / 180;
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
            Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
    return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
}

static List<string> ValidateActivityForCreate(ActivitiesApp.Infrastructure.Models.Activity activity)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(activity.Name))
        errors.Add("Name is required.");
    if (string.IsNullOrWhiteSpace(activity.City))
        errors.Add("City is required.");
    if (activity.MinAge < 0)
        errors.Add("Minimum age cannot be negative.");
    if (activity.MaxAge < activity.MinAge)
        errors.Add("Maximum age must be greater than or equal to minimum age.");
    if (activity.Latitude is < -90 or > 90)
        errors.Add("Latitude must be between -90 and 90.");
    if (activity.Longitude is < -180 or > 180)
        errors.Add("Longitude must be between -180 and 180.");
    if (activity.Activitytime == default)
        errors.Add("Activity time is required.");

    return errors;
}

app.Run();

// Some new change
// Some new change