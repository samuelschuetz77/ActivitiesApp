using ActivitiesApp.Data;
using ActivitiesApp.Shared.Models;
using ActivitiesApp.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace ActivitiesApp.Services;

public class OfflineActivityService : IActivityService
{
    private readonly LocalDbContext _db;
    private readonly HttpClient _http;
    private readonly SyncService _syncService;
    private readonly ActivityCacheService _cache;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<OfflineActivityService> _logger;
    private readonly string? _apiBaseAddress;

    public OfflineActivityService(
        LocalDbContext db,
        HttpClient http,
        SyncService syncService,
        ActivityCacheService cache,
        IConnectivity connectivity,
        ILogger<OfflineActivityService> logger)
    {
        _db = db;
        _http = http;
        _syncService = syncService;
        _cache = cache;
        _connectivity = connectivity;
        _logger = logger;
        _apiBaseAddress = _http.BaseAddress?.ToString().TrimEnd('/');
    }

    public event Action? DataChanged;

    // ─── Activity CRUD (offline-first, cache-backed) ───

    public async Task<Activity> CreateActivityAsync(Activity activity)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var local = new LocalActivity
        {
            Id = activity.Id,
            Name = activity.Name ?? "",
            City = activity.City ?? "",
            Description = activity.Description ?? "",
            Cost = activity.Cost,
            Activitytime = activity.Activitytime,
            Latitude = activity.Latitude,
            Longitude = activity.Longitude,
            MinAge = activity.MinAge,
            MaxAge = activity.MaxAge,
            Category = activity.Category,
            ImageUrl = activity.ImageUrl,
            PlaceId = activity.PlaceId,
            Rating = activity.Rating,
            UpdatedAt = DateTimeOffset.UtcNow,
            SyncState = SyncState.PendingCreate
        };

        _db.Activities.Add(local);
        await _db.SaveChangesAsync();

        var result = NormalizeActivity(ToSharedActivity(local));
        _cache.AddOrUpdate(result);

        _logger.LogInformation("CreateActivityAsync: {Ms}ms", sw.ElapsedMilliseconds);

        // Fire-and-forget sync
        _ = _syncService.SyncAsync();

        return result;
    }

    public Task<Activity?> GetActivityAsync(Guid id)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = NormalizeActivity(_cache.Get(id));
        _logger.LogDebug("GetActivityAsync({Id}): {Ms}ms (cache), hasImage={HasImage}, imageUrl={ImageUrl}",
            id, sw.ElapsedMilliseconds, !string.IsNullOrWhiteSpace(result?.ImageUrl), result?.ImageUrl ?? "");
        return Task.FromResult(result);
    }

    public Task<List<Activity>> ListActivitiesAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = NormalizeActivities(_cache.GetAll());
        _logger.LogDebug("ListActivitiesAsync: {Ms}ms, {Count} items (cache), withImages={ImageCount}",
            sw.ElapsedMilliseconds, result.Count, result.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)));
        return Task.FromResult(result);
    }

    // ─── Discover (return cache immediately, refresh in background) ───

    public async Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters, string? tagName = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Return cached data filtered by distance
        var radiusMiles = radiusMeters / 1609.34;
        var cached = NormalizeActivities(_cache.GetAll())
            .Where(a => GetDistanceMiles(lat, lng, a.Latitude, a.Longitude) <= radiusMiles)
            .Where(a => string.IsNullOrWhiteSpace(tagName) || HasTag(a.Category, tagName))
            .ToList();
        if (cached.Count == 0)
        {
            _logger.LogWarning(
                "DiscoverActivitiesAsync: returning EMPTY cache for tag={Tag} at ({Lat},{Lng}) radius={Radius}mi in {Ms}ms — background refresh will follow",
                tagName ?? "", lat, lng, radiusMiles, sw.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogDebug(
                "DiscoverActivitiesAsync: returning {Count} cached items within {Radius}mi for tag {Tag} in {Ms}ms, withImages={ImageCount}, sampleImages={SampleImages}",
                cached.Count,
                radiusMiles,
                tagName ?? "",
                sw.ElapsedMilliseconds,
                cached.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)),
                string.Join(" | ", cached.Take(5).Select(a => a.ImageUrl ?? "<null>")));
        }

        if (_connectivity.NetworkAccess == NetworkAccess.Internet)
        {
            // Background refresh from REST
            _ = Task.Run(async () =>
            {
                var refreshSw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var tagQuery = string.IsNullOrWhiteSpace(tagName)
                        ? ""
                        : $"&tagName={Uri.EscapeDataString(tagName)}";
                    var activities = await _http.GetFromJsonAsync<List<Activity>>(
                        $"/api/discover?lat={lat}&lng={lng}&radiusMeters={radiusMeters}{tagQuery}");

                    if (activities != null)
                    {
                        foreach (var activity in activities)
                        {
                            var existing = await _db.Activities.FindAsync(activity.Id);
                            if (existing == null)
                            {
                                var local = FromSharedActivity(activity);
                                local.SyncState = SyncState.Synced;
                                _db.Activities.Add(local);
                            }
                            else
                            {
                                existing.Name = activity.Name ?? "";
                                existing.City = activity.City ?? "";
                                existing.Description = activity.Description ?? "";
                                existing.Cost = activity.Cost;
                                existing.Activitytime = activity.Activitytime;
                                existing.Latitude = activity.Latitude;
                                existing.Longitude = activity.Longitude;
                                existing.MinAge = activity.MinAge;
                                existing.MaxAge = activity.MaxAge;
                                existing.Category = activity.Category;
                                existing.ImageUrl = activity.ImageUrl;
                                existing.PlaceId = activity.PlaceId;
                                existing.Rating = activity.Rating;
                                existing.UpdatedAt = activity.UpdatedAt;
                                existing.IsDeleted = activity.IsDeleted;
                                existing.SyncState = SyncState.Synced;
                            }

                            _cache.AddOrUpdate(NormalizeActivity(activity), suppressNotify: true);
                        }

                        await _db.SaveChangesAsync();
                    }

                    var refreshed = NormalizeActivities(_cache.GetAll())
                        .Where(a => GetDistanceMiles(lat, lng, a.Latitude, a.Longitude) <= radiusMiles)
                        .Where(a => string.IsNullOrWhiteSpace(tagName) || HasTag(a.Category, tagName))
                        .ToList();

                    _logger.LogInformation(
                        "DiscoverActivities background refresh complete for tag {Tag}, refreshedCount={Count}, withImages={ImageCount}, elapsed={Ms}ms, sampleImages={SampleImages}",
                        tagName ?? "",
                        refreshed.Count,
                        refreshed.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)),
                        refreshSw.ElapsedMilliseconds,
                        string.Join(" | ", refreshed.Take(3).Select(a => a.ImageUrl ?? "<null>")));
                    // Single notification after all updates, not per-activity
                    DataChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background discover refresh failed for tag {Tag}", tagName ?? "");
                }
            });
        }

        return cached;
    }

    // ─── Google Maps (pass-through via REST, requires online) ───

    public async Task<List<NearbyPlace>> SearchNearbyPlacesAsync(
        double lat, double lng, int radiusMeters, string? type = null, string? keyword = null)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return new List<NearbyPlace>();

        var url = $"/api/places/nearby?lat={lat}&lng={lng}&radiusMeters={radiusMeters}";
        if (!string.IsNullOrEmpty(type)) url += $"&type={Uri.EscapeDataString(type)}";
        if (!string.IsNullOrEmpty(keyword)) url += $"&keyword={Uri.EscapeDataString(keyword)}";
        return await _http.GetFromJsonAsync<List<NearbyPlace>>(url) ?? [];
    }

    public async Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return null;

        try
        {
            return await _http.GetFromJsonAsync<PlaceDetails>(
                $"/api/places/{Uri.EscapeDataString(placeId)}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> ReverseGeocodeAsync(double lat, double lng)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return "Unavailable offline";

        var result = await _http.GetFromJsonAsync<ReverseGeocodeResult>(
            $"/api/geocode/reverse?lat={lat}&lng={lng}");
        return result?.FormattedAddress ?? "Unknown location";
    }

    public async Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return null;

        try
        {
            var result = await _http.GetFromJsonAsync<ZipLookupResult>(
                $"/api/geocode/zip/{Uri.EscapeDataString(zipCode)}");
            if (result != null) result.PostalCode = zipCode;
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ZipLookupResult?> GeocodeAddressAsync(string address)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return null;

        try
        {
            return await _http.GetFromJsonAsync<ZipLookupResult>(
                $"/api/geocode/address?address={Uri.EscapeDataString(address)}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ─── Mapping ───

    private static Activity ToSharedActivity(LocalActivity local)
    {
        return new Activity
        {
            Id = local.Id,
            Name = local.Name,
            City = local.City,
            Description = local.Description,
            Cost = local.Cost,
            Activitytime = local.Activitytime,
            Latitude = local.Latitude,
            Longitude = local.Longitude,
            MinAge = local.MinAge,
            MaxAge = local.MaxAge,
            Category = local.Category,
            ImageUrl = local.ImageUrl,
            PlaceId = local.PlaceId,
            Rating = local.Rating,
            UpdatedAt = local.UpdatedAt,
            IsDeleted = local.IsDeleted
        };
    }

    private static LocalActivity FromSharedActivity(Activity activity)
    {
        return new LocalActivity
        {
            Id = activity.Id,
            Name = activity.Name ?? "",
            City = activity.City ?? "",
            Description = activity.Description ?? "",
            Cost = activity.Cost,
            Activitytime = activity.Activitytime,
            Latitude = activity.Latitude,
            Longitude = activity.Longitude,
            MinAge = activity.MinAge,
            MaxAge = activity.MaxAge,
            Category = activity.Category,
            ImageUrl = activity.ImageUrl,
            PlaceId = activity.PlaceId,
            Rating = activity.Rating,
            UpdatedAt = activity.UpdatedAt,
            IsDeleted = activity.IsDeleted
        };
    }

    private static double GetDistanceMiles(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static bool HasTag(string? category, string tagName)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(tagName))
            return false;

        return category
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(tag => string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase));
    }

    private List<Activity> NormalizeActivities(List<Activity> activities)
    {
        foreach (var activity in activities)
        {
            NormalizeActivity(activity);
        }

        return activities;
    }

    private Activity? NormalizeActivity(Activity? activity)
    {
        if (activity == null)
        {
            return null;
        }

        activity.ImageUrl = ImageUrlResolver.Resolve(activity.ImageUrl, _apiBaseAddress);
        return activity;
    }

    private record ReverseGeocodeResult(string FormattedAddress);
}
