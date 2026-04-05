using System.Net.Http.Json;
using ActivitiesApp.Shared.Models;
using ActivitiesApp.Shared.Services;

namespace ActivitiesApp.Web.Services;

public class ActivityRestClient : IActivityService
{
    private readonly HttpClient _http;
    private readonly ILogger<ActivityRestClient> _logger;

    public ActivityRestClient(HttpClient http, ILogger<ActivityRestClient> logger)
    {
        _http = http;
        _logger = logger;
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
            return await _http.GetFromJsonAsync<Activity>($"/api/activities/{id}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<Activity>> ListActivitiesAsync()
    {
        _logger.LogInformation("REST ListActivities starting");
        var result = await _http.GetFromJsonAsync<List<Activity>>("/api/activities") ?? [];
        _logger.LogInformation("REST ListActivities returned {Count} items", result.Count);
        return result;
    }

    public async Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters, string? tagName = null)
    {
        _logger.LogInformation("REST DiscoverActivities at ({Lat},{Lng}) radius={Radius} tag={Tag}", lat, lng, radiusMeters, tagName ?? "");
        var tagQuery = string.IsNullOrWhiteSpace(tagName) ? "" : $"&tagName={Uri.EscapeDataString(tagName)}";
        var result = await _http.GetFromJsonAsync<List<Activity>>(
            $"/api/discover?lat={lat}&lng={lng}&radiusMeters={radiusMeters}{tagQuery}") ?? [];
        _logger.LogInformation("REST DiscoverActivities returned {Count} items", result.Count);
        return result;
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

    private record ReverseGeocodeResult(string FormattedAddress);
}
