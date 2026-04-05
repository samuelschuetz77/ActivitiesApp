using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Models;
using ActivitiesApp.Infrastructure.Services;
using ActivitiesApp.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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

// ─── Identity DbContext (always Postgres) ───
var identityConnectionString = builder.Configuration.GetConnectionString("ActivitiesDb");
if (string.IsNullOrEmpty(identityConnectionString))
{
    var host = builder.Configuration["POSTGRES_HOST"] ?? defaultPostgresHost;
    var db = builder.Configuration["POSTGRES_DB"] ?? "activitiesdb";
    var user = builder.Configuration["POSTGRES_USER"] ?? "activitiesapp";
    var password = builder.Configuration["POSTGRES_PASSWORD"] ?? "activitiesapp";
    identityConnectionString = $"Host={host};Port=5432;Database={db};Username={user};Password={password}";
}

builder.Services.AddDbContext<AppIdentityDbContext>(options =>
    options.UseNpgsql(identityConnectionString, npgsql =>
        npgsql.MigrationsAssembly("ActivitiesApp.Infrastructure.Migrations")));

// ─── ASP.NET Core Identity ───
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppIdentityDbContext>()
.AddDefaultTokenProviders();

// ─── JWT Authentication ───
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SuperSecretDevKey12345678901234567890";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "ActivitiesApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorization();

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

        try
        {
            await pgContext.Activities.AnyAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            startupLog.LogWarning(ex, "Postgres schema missing after migrations; creating tables directly from the EF model");
            var databaseCreator = pgContext.GetService<IRelationalDatabaseCreator>();
            await databaseCreator.CreateTablesAsync();
        }

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

    try
    {
        // Always migrate and seed Identity when Postgres is available.
        var identityContext = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        await identityContext.Database.MigrateAsync();
        startupLog.LogInformation("Identity migrations applied");

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        string[] roles = ["Admin", "User"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var adminEmail = "admin@activitiesapp.com";
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "Admin",
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, "Admin123!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
    catch (NpgsqlException ex) when (!dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        startupLog.LogWarning(ex, "Identity Postgres is unavailable (DbProvider={DbProvider}). Auth endpoints will not work. " +
            "TODO: Configure POSTGRES_HOST and connection string in Azure App Service when enabling authentication.", dbProvider);
    }
}

app.MapDefaultEndpoints();

// gRPC-Web so HTTP/1.1 callers (Blazor web) can reach gRPC services
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<ActivityGrpcService>().EnableGrpcWeb();

// Temporary: emit test warning/error so Grafana "Error Count and Warnings" panel has data
app.Logger.LogWarning("Test warning for observability");
app.Logger.LogError("Test error for observability");

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

app.MapPost("/api/activities", async (ActivitiesApp.Infrastructure.Models.Activity activity, IActivityDbContext db, HttpContext httpContext) =>
{
    activity.Id = Guid.NewGuid();
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    activity.CreatedByUserId = userId;
    db.Activities.Add(activity);
    await db.SaveChangesAsync();
    activitiesCreatedCounter.Add(1,
        new KeyValuePair<string, object?>("creation_source", "manual"));
    return Results.Created($"/api/activities/{activity.Id}", activity);
}).RequireAuthorization();

app.MapGet("/api/discover", async (double lat, double lng, int? radiusMeters, string? tagName,
    IActivityDbContext db, GooglePlacesService places, ILogger<Program> log) =>
{
    var radius = radiusMeters ?? 16093;
    log.LogInformation("REST DiscoverActivities at ({Lat},{Lng}) radius={Radius}m tag={Tag}", lat, lng, radius, tagName ?? "");

    List<GooglePlacesService.NearbyPlace> googlePlaces;
    try
    {
        googlePlaces = string.IsNullOrWhiteSpace(tagName)
            ? await places.SearchNearbyAsync(lat, lng, radius, type: null, keyword: "fun things to do")
            : await SearchPlacesForTagAsync(lat, lng, radius, tagName, places, log);
        log.LogInformation("REST Discover got {Count} Google places", googlePlaces.Count);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "REST Discover Google search failed for tag {Tag}, falling back to DB only", tagName ?? "");
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
            existing.Category = category;
            existing.Rating = place.Rating;
            existing.Latitude = place.Latitude;
            existing.Longitude = place.Longitude;
            existing.ImageUrl = $"/api/photos/place/{Uri.EscapeDataString(place.PlaceId)}?maxwidth=400";
            updatedExistingCount++;
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
    }

    if (newActivities.Count > 0 || updatedExistingCount > 0)
    {
        await db.SaveChangesAsync();
    }

    log.LogInformation(
        "REST Discover returning {Count} activities with {NewCount} new and {UpdatedCount} updated existing rows",
        results.Count, newActivities.Count, updatedExistingCount);
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

static async Task<List<GooglePlacesService.NearbyPlace>> SearchPlacesForTagAsync(
    double latitude,
    double longitude,
    int radiusMeters,
    string tagName,
    GooglePlacesService places,
    ILogger log)
{
    if (!GooglePlaceTagMapper.TryGetDefinition(tagName, out var tagDefinition) || tagDefinition is null)
    {
        log.LogWarning("REST Discover requested unknown tag {Tag}; falling back to generic search", tagName);
        return await places.SearchNearbyAsync(latitude, longitude, radiusMeters, type: null, keyword: "fun things to do");
    }

    var deduped = new Dictionary<string, GooglePlacesService.NearbyPlace>(StringComparer.Ordinal);

    foreach (var googleType in tagDefinition.SearchTypes)
    {
        try
        {
            var placesForType = await places.SearchNearbyAsync(latitude, longitude, radiusMeters, type: googleType, keyword: null);
            log.LogInformation(
                "REST Discover tag {Tag} for Google type {GoogleType} returned {Count} places",
                tagName, googleType, placesForType.Count);

            if (placesForType.Count > 0)
            {
                log.LogInformation(
                    "REST Discover tag {Tag} type {GoogleType} sample places: {PlaceNames}",
                    tagName,
                    googleType,
                    string.Join(" | ", placesForType.Take(5).Select(p => p.Name)));
            }

            foreach (var place in placesForType)
            {
                if (!string.IsNullOrWhiteSpace(place.PlaceId))
                {
                    deduped[place.PlaceId] = place;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "REST Discover tag {Tag} failed for Google type {GoogleType}", tagName, googleType);
        }
    }

    log.LogInformation("REST Discover tag {Tag} deduped to {Count} places", tagName, deduped.Count);
    if (deduped.Count > 0)
    {
        log.LogInformation(
            "REST Discover tag {Tag} deduped sample: {PlaceNames}",
            tagName,
            string.Join(" | ", deduped.Values.Take(8).Select(p => $"{p.Name} [{string.Join("/", p.Types.Take(3))}]")));
    }

    return deduped.Values.ToList();
}

app.Run();
