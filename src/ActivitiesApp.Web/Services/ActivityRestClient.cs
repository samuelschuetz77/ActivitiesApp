using System.Net.Http.Json;
using ActivitiesApp.Shared.Models;
using ActivitiesApp.Shared.Services;

namespace ActivitiesApp.Web.Services;

public class ActivityRestClient : IActivityService
{
    private readonly HttpClient _http;
    private readonly ILogger<ActivityRestClient> _logger;
    private readonly string? _apiBaseAddress;

    // In-memory discover cache — survives component navigation (this service is scoped per-circuit)
    private readonly Dictionary<string, List<Activity>> _discoverCache = new(StringComparer.OrdinalIgnoreCase);
    private double _cachedLat;
    private double _cachedLng;

    public ActivityRestClient(HttpClient http, ILogger<ActivityRestClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiBaseAddress = _http.BaseAddress?.ToString().TrimEnd('/');
    }

    public async Task<Activity> CreateActivityAsync(Activity activity)
    {
        _logger.LogInformation(
            "REST CreateActivity starting for Name={Name}, City={City}, Category={Category}",
            activity.Name ?? "", activity.City ?? "", activity.Category ?? "");
        var response = await _http.PostAsJsonAsync("/api/activities", activity);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "REST CreateActivity failed with StatusCode={StatusCode} for Name={Name}, City={City}. Response={ResponseBody}",
                (int)response.StatusCode, activity.Name ?? "", activity.City ?? "", responseBody);
        }
        response.EnsureSuccessStatusCode();
        _logger.LogInformation(
            "REST CreateActivity completed for Name={Name}, City={City}",
            activity.Name ?? "", activity.City ?? "");
        return (await response.Content.ReadFromJsonAsync<Activity>())!;
    }

    public async Task<Activity?> GetActivityAsync(Guid id)
    {
        try
        {
            var activity = await _http.GetFromJsonAsync<Activity>($"/api/activities/{id}");
            return NormalizeActivity(activity);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<Activity>> ListActivitiesAsync()
    {
        _logger.LogInformation("REST ListActivities starting");
        var result = NormalizeActivities(await _http.GetFromJsonAsync<List<Activity>>("/api/activities") ?? []);
        _logger.LogInformation("REST ListActivities returned {Count} items, withImages={ImageCount}", result.Count, result.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)));
        return result;
    }

    public async Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters, string? tagName = null)
    {
        var cacheKey = tagName ?? "__all__";

        // Invalidate cache if location moved significantly (~500m)
        if (Math.Abs(lat - _cachedLat) > 0.005 || Math.Abs(lng - _cachedLng) > 0.005)
        {
            _logger.LogInformation("REST DiscoverCache invalidated: location moved from ({OldLat},{OldLng}) to ({NewLat},{NewLng}), clearing {Count} cached tags",
                _cachedLat, _cachedLng, lat, lng, _discoverCache.Count);
            _discoverCache.Clear();
            _cachedLat = lat;
            _cachedLng = lng;
        }

        // Return cached result if available (back-navigation, tag re-click)
        if (_discoverCache.TryGetValue(cacheKey, out var cached))
        {
            _logger.LogInformation("REST DiscoverActivities cache hit: tag={Tag}, count={Count}, withImages={ImageCount}",
                tagName ?? "", cached.Count, cached.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)));
            return cached;
        }

        _logger.LogInformation("REST DiscoverActivities at ({Lat},{Lng}) radius={Radius} tag={Tag}", lat, lng, radiusMeters, tagName ?? "");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var tagQuery = string.IsNullOrWhiteSpace(tagName) ? "" : $"&tagName={Uri.EscapeDataString(tagName)}";
            var result = NormalizeActivities(await _http.GetFromJsonAsync<List<Activity>>(
                $"/api/discover?lat={lat}&lng={lng}&radiusMeters={radiusMeters}{tagQuery}") ?? []);
            var fastImages = result.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl) && a.ImageUrl.Contains("/api/photos?r="));
            var slowImages = result.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl) && a.ImageUrl.Contains("/api/photos/place/"));
            _logger.LogInformation(
                "REST DiscoverActivities completed: tag={Tag}, count={Count}, withImages={ImageCount}, fastImages={Fast}, slowImages={Slow}, elapsed={Ms}ms, sample={Sample}",
                tagName ?? "",
                result.Count,
                result.Count(a => !string.IsNullOrWhiteSpace(a.ImageUrl)),
                fastImages,
                slowImages,
                sw.ElapsedMilliseconds,
                string.Join(" | ", result.Take(5).Select(a => $"{a.Name} [{a.Category}]")));

            _discoverCache[cacheKey] = result;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "REST DiscoverActivities FAILED: tag={Tag}, elapsed={Ms}ms, error={Error}",
                tagName ?? "", sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    public async Task<List<NearbyPlace>> SearchNearbyPlacesAsync(
        double lat, double lng, int radiusMeters, string? type = null, string? keyword = null)
    {
        var url = $"/api/places/nearby?lat={lat}&lng={lng}&radiusMeters={radiusMeters}";
        if (!string.IsNullOrEmpty(type)) url += $"&type={Uri.EscapeDataString(type)}";
        if (!string.IsNullOrEmpty(keyword)) url += $"&keyword={Uri.EscapeDataString(keyword)}";
        return await _http.GetFromJsonAsync<List<NearbyPlace>>(url) ?? [];
    }

    public async Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId)
    {
        try
        {
            return await _http.GetFromJsonAsync<PlaceDetails>($"/api/places/{Uri.EscapeDataString(placeId)}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> ReverseGeocodeAsync(double lat, double lng)
    {
        var result = await _http.GetFromJsonAsync<ReverseGeocodeResult>($"/api/geocode/reverse?lat={lat}&lng={lng}");
        return result?.FormattedAddress ?? "Unknown location";
    }

    public async Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<ZipLookupResult>($"/api/geocode/zip/{Uri.EscapeDataString(zipCode)}");
            if (result != null) result.PostalCode = zipCode;
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public event Action? DataChanged;

    public async Task<ZipLookupResult?> GeocodeAddressAsync(string address)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<ZipLookupResult>(
                $"/api/geocode/address?address={Uri.EscapeDataString(address)}");
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
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

    public async Task<QuotaStatusResponse?> GetQuotaStatusAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<QuotaStatusResponse>("/api/quota/status");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch quota status");
            return null;
        }
    }

    private record ReverseGeocodeResult(string FormattedAddress);
}
