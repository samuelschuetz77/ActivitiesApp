using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActivitiesApp.Api.Services;

public class GooglePlacesService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<GooglePlacesService> _logger;
    private readonly Counter<long> _requestCounter;

    // Daily quota tracking — keyed by "{date}:{apiType}"
    private static readonly ConcurrentDictionary<string, int> DailyUsage = new();

    // Quota limits matching GCP console settings
    public static readonly Dictionary<string, int> QuotaLimits = new()
    {
        ["nearby_search"] = 50,
        ["place_details"] = 50,
        ["photo"] = 100,
        ["geocode"] = 100
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public GooglePlacesService(HttpClient http, IConfiguration config, ILogger<GooglePlacesService> logger, IMeterFactory meterFactory)
    {
        _http = http;
        _apiKey = config["GoogleMaps:ApiKey"]
            ?? throw new InvalidOperationException("GoogleMaps:ApiKey is not configured.");
        _logger = logger;

        var meter = meterFactory.Create("ActivitiesApp.GoogleApi");
        _requestCounter = meter.CreateCounter<long>(
            "google_api_requests_total",
            unit: "{request}",
            description: "Total Google API requests by type");
    }

    private void TrackRequest(string apiType)
    {
        _requestCounter.Add(1, new KeyValuePair<string, object?>("api_type", apiType));
        var key = $"{DateTime.UtcNow:yyyy-MM-dd}:{apiType}";
        DailyUsage.AddOrUpdate(key, 1, (_, count) => count + 1);
    }

    public static Dictionary<string, QuotaStatus> GetQuotaStatus()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var result = new Dictionary<string, QuotaStatus>();
        foreach (var (apiType, limit) in QuotaLimits)
        {
            var key = $"{today}:{apiType}";
            DailyUsage.TryGetValue(key, out var used);
            result[apiType] = new QuotaStatus(used, limit);
        }
        return result;
    }

    public record QuotaStatus(int Used, int Limit);

    public async Task<List<NearbyPlace>> SearchNearbyAsync(
        double lat, double lng, int radiusMeters, string? type = null, string? keyword = null)
    {
        var url = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
                  $"?location={lat},{lng}&radius={radiusMeters}&key={_apiKey}";

        if (!string.IsNullOrEmpty(type))
            url += $"&type={Uri.EscapeDataString(type)}";
        if (!string.IsNullOrEmpty(keyword))
            url += $"&keyword={Uri.EscapeDataString(keyword)}";

        TrackRequest("nearby_search");
        _logger.LogInformation(
            "Google Nearby Search request: lat={Lat}, lng={Lng}, radius={RadiusMeters}, type={Type}, keyword={Keyword}",
            lat, lng, radiusMeters, type ?? "", keyword ?? "");
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<NearbySearchResponse>(JsonOptions);

        if (json?.Status != "OK" && json?.Status != "ZERO_RESULTS")
        {
            _logger.LogWarning("Google Nearby Search returned status: {Status}", json?.Status);
        }

        _logger.LogInformation("Google Nearby Search returned {Count} results, status={Status}",
            json?.Results?.Count ?? 0, json?.Status);

        var results = new List<NearbyPlace>();
        foreach (var r in json?.Results ?? [])
        {
            results.Add(new NearbyPlace
            {
                PlaceId = r.PlaceId ?? "",
                Name = r.Name ?? "",
                Vicinity = r.Vicinity ?? "",
                Latitude = r.Geometry?.Location?.Lat ?? 0,
                Longitude = r.Geometry?.Location?.Lng ?? 0,
                Rating = r.Rating,
                UserRatingsTotal = r.UserRatingsTotal,
                IsOpenNow = r.OpeningHours?.OpenNow ?? false,
                Types = r.Types ?? [],
                PriceLevel = r.PriceLevel,
                PhotoUrl = r.Photos?.FirstOrDefault()?.PhotoReference != null
                    ? BuildPhotoUrl(r.Photos[0].PhotoReference, 400)
                    : ""
            });
        }

        return results;
    }

    public async Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId)
    {
        var fields = "place_id,name,formatted_address,formatted_phone_number,website," +
                     "rating,user_ratings_total,geometry,photos,reviews,opening_hours,price_level,types";

        var url = $"https://maps.googleapis.com/maps/api/place/details/json" +
                  $"?place_id={Uri.EscapeDataString(placeId)}&fields={fields}&key={_apiKey}";

        TrackRequest("place_details");
        _logger.LogInformation("Google Place Details request for {PlaceId}", placeId);
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<PlaceDetailsApiResponse>(JsonOptions);

        if (json?.Status != "OK")
        {
            _logger.LogWarning("Google Place Details returned status: {Status}", json?.Status);
            return null;
        }

        var r = json.Result;
        if (r == null) return null;

        return new PlaceDetails
        {
            PlaceId = r.PlaceId ?? placeId,
            Name = r.Name ?? "",
            FormattedAddress = r.FormattedAddress ?? "",
            FormattedPhone = r.FormattedPhoneNumber ?? "",
            Website = r.Website ?? "",
            Rating = r.Rating,
            UserRatingsTotal = r.UserRatingsTotal,
            Latitude = r.Geometry?.Location?.Lat ?? 0,
            Longitude = r.Geometry?.Location?.Lng ?? 0,
            PhotoUrls = r.Photos?.Select(p => BuildPhotoUrl(p.PhotoReference, 800)).ToList() ?? [],
            Reviews = r.Reviews?.Select(rev => new PlaceReviewData
            {
                AuthorName = rev.AuthorName ?? "",
                Rating = rev.Rating,
                Text = rev.Text ?? "",
                RelativeTime = rev.RelativeTimeDescription ?? ""
            }).ToList() ?? [],
            OpeningHoursSummary = r.OpeningHours?.WeekdayText != null
                ? string.Join("; ", r.OpeningHours.WeekdayText)
                : "",
            PriceLevel = r.PriceLevel,
            Types = r.Types ?? []
        };
    }

    public async Task<string> ReverseGeocodeAsync(double lat, double lng)
    {
        var url = $"https://maps.googleapis.com/maps/api/geocode/json" +
                  $"?latlng={lat},{lng}&key={_apiKey}";

        TrackRequest("geocode");
        _logger.LogInformation("Google Reverse Geocode request for ({Lat},{Lng})", lat, lng);
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<GeocodeResponse>(JsonOptions);
        var address = json?.Results?.FirstOrDefault()?.FormattedAddress ?? "Unknown location";
        _logger.LogInformation("Google Reverse Geocode resolved ({Lat},{Lng}) to {Address}", lat, lng, address);
        return address;
    }

    public async Task<(double Latitude, double Longitude, string FormattedAddress)?> GeocodeAddressAsync(string address)
    {
        var url = $"https://maps.googleapis.com/maps/api/geocode/json" +
                  $"?address={Uri.EscapeDataString(address)}&key={_apiKey}";

        TrackRequest("geocode");
        _logger.LogInformation("Google address geocode request for {Address}", address);
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<GeocodeResponse>(JsonOptions);
        var result = json?.Results?.FirstOrDefault();
        var location = result?.Geometry?.Location;

        if (location == null)
        {
            _logger.LogWarning("Google address geocode returned no result for {Address}", address);
            return null;
        }

        var formatted = result?.FormattedAddress ?? address;
        _logger.LogInformation("Google address geocode resolved to ({Lat},{Lng}) {FormattedAddress}",
            location.Lat, location.Lng, formatted);
        return (location.Lat, location.Lng, formatted);
    }

    public async Task<(double Latitude, double Longitude, string FormattedAddress)?> GeocodePostalCodeAsync(string postalCode)
    {
        var normalizedPostalCode = postalCode.Trim();
        var url = $"https://maps.googleapis.com/maps/api/geocode/json" +
                  $"?components=postal_code:{Uri.EscapeDataString(normalizedPostalCode)}|country:US&key={_apiKey}";

        TrackRequest("geocode");
        _logger.LogInformation("Google ZIP geocode request for postal code {PostalCode}", normalizedPostalCode);
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<GeocodeResponse>(JsonOptions);
        var result = json?.Results?.FirstOrDefault();
        var location = result?.Geometry?.Location;

        if (location == null)
        {
            _logger.LogWarning("Google ZIP geocode returned no result for postal code {PostalCode}", normalizedPostalCode);
            return null;
        }

        var address = result?.FormattedAddress ?? normalizedPostalCode;
        _logger.LogInformation(
            "Google ZIP geocode resolved {PostalCode} to ({Lat},{Lng}) {Address}",
            normalizedPostalCode, location.Lat, location.Lng, address);

        return (location.Lat, location.Lng, address);
    }

    /// <summary>
    /// Fetches a Google Places photo server-side, keeping the API key hidden from clients.
    /// </summary>
    public async Task<byte[]?> FetchPhotoAsync(string photoReference, int maxWidth)
    {
        var url = $"https://maps.googleapis.com/maps/api/place/photo" +
                  $"?maxwidth={maxWidth}&photoreference={Uri.EscapeDataString(photoReference)}&key={_apiKey}";

        TrackRequest("photo");
        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadAsByteArrayAsync();
    }

    private string BuildPhotoUrl(string photoReference, int maxWidth)
    {
        return $"/api/photos?r={Uri.EscapeDataString(photoReference)}&maxwidth={maxWidth}";
    }

    // ─── DTOs for Google API JSON responses ───

    public record GeocodeResponse
    {
        public List<GeocodeResult>? Results { get; init; }
    }

    public record GeocodeResult
    {
        public string? FormattedAddress { get; init; }
        public GeometryData? Geometry { get; init; }
    }

    public record NearbySearchResponse
    {
        public string? Status { get; init; }
        public List<PlaceApiResult>? Results { get; init; }
    }

    public record PlaceDetailsApiResponse
    {
        public string? Status { get; init; }
        public PlaceApiDetailResult? Result { get; init; }
    }

    public record PlaceApiResult
    {
        public string? PlaceId { get; init; }
        public string? Name { get; init; }
        public string? Vicinity { get; init; }
        public GeometryData? Geometry { get; init; }
        public double Rating { get; init; }
        public int UserRatingsTotal { get; init; }
        public OpeningHoursData? OpeningHours { get; init; }
        public List<string>? Types { get; init; }
        public int PriceLevel { get; init; }
        public List<PhotoData>? Photos { get; init; }
    }

    public record PlaceApiDetailResult
    {
        public string? PlaceId { get; init; }
        public string? Name { get; init; }
        public string? FormattedAddress { get; init; }
        public string? FormattedPhoneNumber { get; init; }
        public string? Website { get; init; }
        public double Rating { get; init; }
        public int UserRatingsTotal { get; init; }
        public GeometryData? Geometry { get; init; }
        public List<PhotoData>? Photos { get; init; }
        public List<ReviewData>? Reviews { get; init; }
        public OpeningHoursDetailData? OpeningHours { get; init; }
        public int PriceLevel { get; init; }
        public List<string>? Types { get; init; }
    }

    public record GeometryData { public LocationData? Location { get; init; } }
    public record LocationData { public double Lat { get; init; } public double Lng { get; init; } }
    public record OpeningHoursData { public bool OpenNow { get; init; } }
    public record OpeningHoursDetailData
    {
        public bool OpenNow { get; init; }
        public List<string>? WeekdayText { get; init; }
    }
    public record PhotoData { public string PhotoReference { get; init; } = ""; }
    public record ReviewData
    {
        public string? AuthorName { get; init; }
        public double Rating { get; init; }
        public string? Text { get; init; }
        public string? RelativeTimeDescription { get; init; }
    }

    // ─── Clean output models ───

    public class NearbyPlace
    {
        public string PlaceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Vicinity { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Rating { get; set; }
        public int UserRatingsTotal { get; set; }
        public bool IsOpenNow { get; set; }
        public List<string> Types { get; set; } = [];
        public int PriceLevel { get; set; }
        public string PhotoUrl { get; set; } = "";
    }

    public class PlaceDetails
    {
        public string PlaceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string FormattedAddress { get; set; } = "";
        public string FormattedPhone { get; set; } = "";
        public string Website { get; set; } = "";
        public double Rating { get; set; }
        public int UserRatingsTotal { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public List<string> PhotoUrls { get; set; } = [];
        public List<PlaceReviewData> Reviews { get; set; } = [];
        public string OpeningHoursSummary { get; set; } = "";
        public int PriceLevel { get; set; }
        public List<string> Types { get; set; } = [];
    }

    public class PlaceReviewData
    {
        public string AuthorName { get; set; } = "";
        public double Rating { get; set; }
        public string Text { get; set; } = "";
        public string RelativeTime { get; set; } = "";
    }
}
