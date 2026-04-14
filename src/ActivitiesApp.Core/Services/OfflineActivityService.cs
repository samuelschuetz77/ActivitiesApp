using ActivitiesApp.Data;
using ActivitiesApp.Shared.Models;
using ActivitiesApp.Shared.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace ActivitiesApp.Services;

public class OfflineActivityService : IActivityService
{
    private readonly ILocalActivityStore _store;
    private readonly HttpClient _http;
    private readonly SyncService _syncService;
    private readonly ActivityCacheService _cache;
    private readonly INetworkStatus _networkStatus;
    private readonly ILogger<OfflineActivityService> _logger;
    private readonly string? _apiBaseAddress;

    public OfflineActivityService(
        ILocalActivityStore store,
        HttpClient http,
        SyncService syncService,
        ActivityCacheService cache,
        INetworkStatus networkStatus,
        ILogger<OfflineActivityService> logger)
    {
        _store = store;
        _http = http;
        _syncService = syncService;
        _cache = cache;
        _networkStatus = networkStatus;
        _logger = logger;
        _apiBaseAddress = _http.BaseAddress?.ToString().TrimEnd('/');
    }

    public event Action? DataChanged;

    public async Task<Activity> CreateActivityAsync(Activity activity)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var local = ActivityMapping.ToLocalActivity(activity, SyncState.PendingCreate);
        local.UpdatedAt = DateTimeOffset.UtcNow;

        await _store.SaveActivityAsync(local);

        var result = NormalizeActivity(ActivityMapping.ToActivity(local))!;
        _cache.AddOrUpdate(result);
        _logger.LogInformation("CreateActivityAsync: {Ms}ms", sw.ElapsedMilliseconds);

        _ = _syncService.SyncAsync();

        return result;
    }

    public Task<Activity?> GetActivityAsync(Guid id)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = NormalizeActivity(_cache.Get(id));
        _logger.LogDebug(
            "GetActivityAsync({Id}): {Ms}ms (cache), hasImage={HasImage}, imageUrl={ImageUrl}",
            id,
            sw.ElapsedMilliseconds,
            !string.IsNullOrWhiteSpace(result?.ImageUrl),
            result?.ImageUrl ?? "");
        return Task.FromResult(result);
    }

    public Task<List<Activity>> ListActivitiesAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = NormalizeActivities(_cache.GetAll());
        _logger.LogDebug(
            "ListActivitiesAsync: {Ms}ms, {Count} items (cache), withImages={ImageCount}",
            sw.ElapsedMilliseconds,
            result.Count,
            result.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)));
        return Task.FromResult(result);
    }

    public async Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters, string? tagName = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var radiusMiles = radiusMeters / 1609.34;
        var cached = NormalizeActivities(_cache.GetAll())
            .Where(a => GetDistanceMiles(lat, lng, a.Latitude, a.Longitude) <= radiusMiles)
            .Where(a => string.IsNullOrWhiteSpace(tagName) || HasTag(a.Category, tagName))
            .ToList();

        if (cached.Count == 0)
        {
            _logger.LogWarning(
                "DiscoverActivitiesAsync: returning EMPTY cache for tag={Tag} at ({Lat},{Lng}) radius={Radius}mi in {Ms}ms - background refresh will follow",
                tagName ?? "",
                lat,
                lng,
                radiusMiles,
                sw.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogDebug(
                "DiscoverActivitiesAsync: returning {Count} cached items within {Radius}mi for tag {Tag} in {Ms}ms, withImages={ImageCount}",
                cached.Count,
                radiusMiles,
                tagName ?? "",
                sw.ElapsedMilliseconds,
                cached.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)));
        }

        if (_networkStatus.HasInternet)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var tagQuery = string.IsNullOrWhiteSpace(tagName)
                        ? ""
                        : $"&tagName={Uri.EscapeDataString(tagName)}";
                    var activities = await _http.GetFromJsonAsync<List<Activity>>(
                        $"/api/discover?lat={lat}&lng={lng}&radiusMeters={radiusMeters}{tagQuery}");

                    if (activities != null)
                    {
                        await _store.SaveActivitiesAsync(
                            activities.Select(activity => ActivityMapping.ToLocalActivity(activity, SyncState.Synced)),
                            CancellationToken.None);

                        foreach (var refreshedActivity in activities)
                        {
                            _cache.AddOrUpdate(NormalizeActivity(refreshedActivity)!, suppressNotify: true);
                        }
                    }

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

    public async Task<List<NearbyPlace>> SearchNearbyPlacesAsync(double lat, double lng, int radiusMeters, string? type = null, string? keyword = null)
    {
        if (!_networkStatus.HasInternet)
        {
            return [];
        }

        var url = $"/api/places/nearby?lat={lat}&lng={lng}&radiusMeters={radiusMeters}";
        if (!string.IsNullOrEmpty(type))
        {
            url += $"&type={Uri.EscapeDataString(type)}";
        }

        if (!string.IsNullOrEmpty(keyword))
        {
            url += $"&keyword={Uri.EscapeDataString(keyword)}";
        }

        return await _http.GetFromJsonAsync<List<NearbyPlace>>(url) ?? [];
    }

    public async Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId)
    {
        if (!_networkStatus.HasInternet)
        {
            return null;
        }

        try
        {
            return await _http.GetFromJsonAsync<PlaceDetails>($"/api/places/{Uri.EscapeDataString(placeId)}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> ReverseGeocodeAsync(double lat, double lng)
    {
        if (!_networkStatus.HasInternet)
        {
            return "Unavailable offline";
        }

        var result = await _http.GetFromJsonAsync<ReverseGeocodeResult>($"/api/geocode/reverse?lat={lat}&lng={lng}");
        return result?.FormattedAddress ?? "Unknown location";
    }

    public async Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode)
    {
        if (!_networkStatus.HasInternet)
        {
            return null;
        }

        try
        {
            var result = await _http.GetFromJsonAsync<ZipLookupResult>($"/api/geocode/zip/{Uri.EscapeDataString(zipCode)}");
            if (result != null)
            {
                result.PostalCode = zipCode;
            }

            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ZipLookupResult?> GeocodeAddressAsync(string address)
    {
        if (!_networkStatus.HasInternet)
        {
            return null;
        }

        try
        {
            return await _http.GetFromJsonAsync<ZipLookupResult>($"/api/geocode/address?address={Uri.EscapeDataString(address)}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
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

    private static double GetDistanceMiles(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusMiles = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMiles * c;
    }

    private static bool HasTag(string? category, string tagName)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        return category
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(tag => string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ReverseGeocodeResult(string FormattedAddress);
}
