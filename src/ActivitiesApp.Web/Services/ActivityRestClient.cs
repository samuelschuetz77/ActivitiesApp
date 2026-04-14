using System.Net.Http.Headers;
using System.Net.Http.Json;
using ActivitiesApp.Shared.Models;
using ActivitiesApp.Shared.Services;
using Microsoft.Identity.Web;

namespace ActivitiesApp.Web.Services;

public class ActivityRestClient : IActivityService
{
    private const string ApiScope = "api://6d3dc4ee-33ce-4656-95c8-702a38464687/access_as_user";

    private readonly HttpClient _http;
    private readonly ILogger<ActivityRestClient> _logger;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly string? _apiBaseAddress;

    // In-memory discover cache — survives component navigation (this service is scoped per-circuit)
    private readonly Dictionary<string, List<Activity>> _discoverCache = new(StringComparer.OrdinalIgnoreCase);
    private double _cachedLat;
    private double _cachedLng;

    public ActivityRestClient(HttpClient http, ILogger<ActivityRestClient> logger, ITokenAcquisition tokenAcquisition)
    {
        _http = http;
        _logger = logger;
        _tokenAcquisition = tokenAcquisition;
        _apiBaseAddress = _http.BaseAddress?.ToString().TrimEnd('/');
    }

    public async Task<Activity> CreateActivityAsync(Activity activity)
    {
        _logger.LogInformation(
            "REST CreateActivity starting for Name={Name}, City={City}, Category={Category}",
            activity.Name ?? "", activity.City ?? "", activity.Category ?? "");

        string token;
        try
        {
            token = await _tokenAcquisition.GetAccessTokenForUserAsync([ApiScope]);
        }
        catch (MicrosoftIdentityWebChallengeUserException ex)
        {
            var msg = $"Token acquisition failed — re-authentication required. MSAL error: {ex.MsalUiRequiredException?.ErrorCode} — {ex.Message}. Try signing out and back in.";
            _logger.LogError(ex, "REST CreateActivity token acquisition challenge for Name={Name}", activity.Name ?? "");
            throw new InvalidOperationException(msg, ex);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("MsalUiRequired") || ex.GetType().Name.Contains("MsalService"))
        {
            var msg = $"Token acquisition failed — {ex.GetType().Name}: {ex.Message}. Try signing out and back in.";
            _logger.LogError(ex, "REST CreateActivity token acquisition failed for Name={Name}", activity.Name ?? "");
            throw new InvalidOperationException(msg, ex);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/activities");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(activity);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            var errorMessage = BuildErrorMessage(response, responseBody);
            _logger.LogError(
                "REST CreateActivity failed with StatusCode={StatusCode} for Name={Name}, City={City}. Diagnosis={Diagnosis}. Response={ResponseBody}",
                (int)response.StatusCode, activity.Name ?? "", activity.City ?? "", errorMessage, responseBody);
            throw new InvalidOperationException(errorMessage);
        }
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

    private static string BuildErrorMessage(HttpResponseMessage response, string responseBody)
    {
        var statusCode = (int)response.StatusCode;
        var body = Truncate(responseBody.Trim(), 300);
        return statusCode switch
        {
            400 => $"Validation error (400): The server rejected the activity data. Response: {body}",
            401 => Diagnose401(response, responseBody),
            403 => "Access denied (403): Your account does not have permission to create activities.",
            404 => "API endpoint not found (404): The create-activity endpoint does not exist on the server. Check API deployment.",
            409 => $"Conflict (409): A duplicate activity may already exist. Response: {body}",
            _ => $"HTTP {statusCode}: Unexpected error from API. Response: {body}"
        };
    }

    // Parses the WWW-Authenticate header and response body to give a specific 401 diagnosis.
    // Raw WWW-Authenticate is always appended so the full error is visible for debugging.
    private static string Diagnose401(HttpResponseMessage response, string responseBody)
    {
        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        var desc = ExtractWwwAuthParam(wwwAuth, "error_description");
        var error = ExtractWwwAuthParam(wwwAuth, "error");
        var body = Truncate(responseBody.Trim(), 300);
        var raw = $" | RAW WWW-Authenticate: [{wwwAuth}] | Body: [{body}]";

        // 1. Token completely absent — Authorization header never reached the API
        if (string.IsNullOrEmpty(wwwAuth) && string.IsNullOrEmpty(responseBody))
            return $"401 — No token reached the API. The Bearer token may have been stripped by a reverse proxy or MSAL silent-refresh returned null. Check ActivityRestClient logs for 'token acquisition'.{raw}";

        // 2. Token expired (long Blazor sessions, stale cache)
        if (desc.Contains("lifetime", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
            (error == "invalid_token" && !desc.Contains("nbf", StringComparison.OrdinalIgnoreCase) && desc.Contains("expir", StringComparison.OrdinalIgnoreCase)))
            return $"401 — Token expired. MSAL should auto-refresh; if this persists the token cache may be corrupted — sign out and back in. API said: {desc}{raw}";

        // 3. Wrong audience — aud claim mismatch or App ID URI not configured in Azure
        if (desc.Contains("audience", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("aud", StringComparison.OrdinalIgnoreCase))
            return $"401 — Wrong audience. Token 'aud' claim doesn't match expected audience (api://6d3dc4ee-33ce-4656-95c8-702a38464687). Fix: In Azure portal → App Registration → 'Expose an API' → verify Application ID URI = api://6d3dc4ee-33ce-4656-95c8-702a38464687 and scope 'access_as_user' exists and is enabled. API said: {desc}{raw}";

        // 4. Issuer/tenant mismatch
        if (desc.Contains("issuer", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("iss", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("tenant", StringComparison.OrdinalIgnoreCase))
            return $"401 — Issuer/tenant mismatch. Token was issued by a tenant the API does not accept. API said: {desc}{raw}";

        // 5. Signature validation failed — key rotation or tampered token
        if (desc.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("IDX10503", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("IDX10511", StringComparison.OrdinalIgnoreCase))
            return $"401 — Token signature invalid. Signing keys may have just rotated (auto-heals in minutes) or the token was tampered. API said: {desc}{raw}";

        // 6. Token not yet valid — server clock skew (nbf claim)
        if (desc.Contains("nbf", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("not before", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("IDX10501", StringComparison.OrdinalIgnoreCase))
            return $"401 — Token 'not before' (nbf) is in the future. Clock skew between Entra ID issuer and API host. API said: {desc}{raw}";

        // 7. Malformed Authorization header or wrong Bearer format
        if (error == "invalid_request" ||
            desc.Contains("malformed", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("invalid header", StringComparison.OrdinalIgnoreCase))
            return $"401 — Malformed Authorization header. Bearer scheme or token format rejected by API. API said: {desc}{raw}";

        // 8. Catch-all — full raw details for debugging
        return $"401 — Token rejected (unrecognized reason). API said: {desc}{raw}";
    }

    private static string ExtractWwwAuthParam(string header, string param)
    {
        // Parses: Bearer error="...", error_description="..."
        var key = param + "=\"";
        var start = header.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        start += key.Length;
        var end = header.IndexOf('"', start);
        return end < 0 ? string.Empty : header[start..end];
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private record ReverseGeocodeResult(string FormattedAddress);
}
