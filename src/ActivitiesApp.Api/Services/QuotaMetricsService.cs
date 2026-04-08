using System.Diagnostics.Metrics;
using System.Text.Json;

namespace ActivitiesApp.Api.Services;

/// <summary>
/// Background service that periodically publishes Google API quota usage as Prometheus gauges.
/// Fetches both local counts and remote Azure API counts, exposing combined metrics
/// so Grafana can display aggregate quota usage across all environments.
/// </summary>
public class QuotaMetricsService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QuotaMetricsService> _logger;
    private readonly IMeterFactory _meterFactory;

    // Gauge values updated every cycle — ObservableGauge reads these on scrape
    private static int _localNearbySearch, _localPlaceDetails, _localPhoto, _localGeocode;
    private static int _azureNearbySearch, _azurePlaceDetails, _azurePhoto, _azureGeocode;
    private static int _combinedNearbySearch, _combinedPlaceDetails, _combinedPhoto, _combinedGeocode;

    public QuotaMetricsService(
        IHttpClientFactory httpClientFactory,
        ILogger<QuotaMetricsService> logger,
        IMeterFactory meterFactory)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _meterFactory = meterFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var meter = _meterFactory.Create("ActivitiesApp.GoogleApi");

        // Observable gauges — Prometheus reads these on every scrape
        meter.CreateObservableGauge("google_quota_used",
            () => new[]
            {
                new Measurement<int>(_combinedNearbySearch, new KeyValuePair<string, object?>("api_type", "nearby_search"), new KeyValuePair<string, object?>("source", "combined")),
                new Measurement<int>(_combinedPlaceDetails, new KeyValuePair<string, object?>("api_type", "place_details"), new KeyValuePair<string, object?>("source", "combined")),
                new Measurement<int>(_combinedPhoto, new KeyValuePair<string, object?>("api_type", "photo"), new KeyValuePair<string, object?>("source", "combined")),
                new Measurement<int>(_combinedGeocode, new KeyValuePair<string, object?>("api_type", "geocode"), new KeyValuePair<string, object?>("source", "combined")),

                new Measurement<int>(_localNearbySearch, new KeyValuePair<string, object?>("api_type", "nearby_search"), new KeyValuePair<string, object?>("source", "local")),
                new Measurement<int>(_localPlaceDetails, new KeyValuePair<string, object?>("api_type", "place_details"), new KeyValuePair<string, object?>("source", "local")),
                new Measurement<int>(_localPhoto, new KeyValuePair<string, object?>("api_type", "photo"), new KeyValuePair<string, object?>("source", "local")),
                new Measurement<int>(_localGeocode, new KeyValuePair<string, object?>("api_type", "geocode"), new KeyValuePair<string, object?>("source", "local")),

                new Measurement<int>(_azureNearbySearch, new KeyValuePair<string, object?>("api_type", "nearby_search"), new KeyValuePair<string, object?>("source", "azure")),
                new Measurement<int>(_azurePlaceDetails, new KeyValuePair<string, object?>("api_type", "place_details"), new KeyValuePair<string, object?>("source", "azure")),
                new Measurement<int>(_azurePhoto, new KeyValuePair<string, object?>("api_type", "photo"), new KeyValuePair<string, object?>("source", "azure")),
                new Measurement<int>(_azureGeocode, new KeyValuePair<string, object?>("api_type", "geocode"), new KeyValuePair<string, object?>("source", "azure")),
            },
            unit: "{request}",
            description: "Current daily Google API quota usage");

        meter.CreateObservableGauge("google_quota_limit",
            () => new[]
            {
                new Measurement<int>(GooglePlacesService.QuotaLimits["nearby_search"], new KeyValuePair<string, object?>("api_type", "nearby_search")),
                new Measurement<int>(GooglePlacesService.QuotaLimits["place_details"], new KeyValuePair<string, object?>("api_type", "place_details")),
                new Measurement<int>(GooglePlacesService.QuotaLimits["photo"], new KeyValuePair<string, object?>("api_type", "photo")),
                new Measurement<int>(GooglePlacesService.QuotaLimits["geocode"], new KeyValuePair<string, object?>("api_type", "geocode")),
            },
            unit: "{request}",
            description: "Google API daily quota limits");

        meter.CreateObservableGauge("google_quota_percentage",
            () =>
            {
                static double Pct(int used, int limit) => limit > 0 ? Math.Round((double)used / limit * 100, 1) : 0;
                return new[]
                {
                    new Measurement<double>(Pct(_combinedNearbySearch, GooglePlacesService.QuotaLimits["nearby_search"]),
                        new KeyValuePair<string, object?>("api_type", "nearby_search")),
                    new Measurement<double>(Pct(_combinedPlaceDetails, GooglePlacesService.QuotaLimits["place_details"]),
                        new KeyValuePair<string, object?>("api_type", "place_details")),
                    new Measurement<double>(Pct(_combinedPhoto, GooglePlacesService.QuotaLimits["photo"]),
                        new KeyValuePair<string, object?>("api_type", "photo")),
                    new Measurement<double>(Pct(_combinedGeocode, GooglePlacesService.QuotaLimits["geocode"]),
                        new KeyValuePair<string, object?>("api_type", "geocode")),
                };
            },
            unit: "%",
            description: "Google API daily quota usage percentage (combined local + Azure)");

        // Update loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                UpdateFromLocal();
                await UpdateFromAzureAsync(stoppingToken);
                UpdateCombined();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "QuotaMetricsService cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private void UpdateFromLocal()
    {
        var local = GooglePlacesService.GetQuotaStatus();
        _localNearbySearch = local["nearby_search"].Used;
        _localPlaceDetails = local["place_details"].Used;
        _localPhoto = local["photo"].Used;
        _localGeocode = local["geocode"].Used;
    }

    private async Task UpdateFromAzureAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AzureApi");
            var response = await client.GetAsync("/api/quota/status", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Azure quota fetch returned {StatusCode}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            _azureNearbySearch = json.GetProperty("nearbySearch").GetProperty("used").GetInt32();
            _azurePlaceDetails = json.GetProperty("placeDetails").GetProperty("used").GetInt32();
            _azurePhoto = json.GetProperty("photos").GetProperty("used").GetInt32();
            _azureGeocode = json.GetProperty("geocoding").GetProperty("used").GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Azure quota fetch failed (Azure API may be unavailable)");
        }
    }

    private void UpdateCombined()
    {
        _combinedNearbySearch = _localNearbySearch + _azureNearbySearch;
        _combinedPlaceDetails = _localPlaceDetails + _azurePlaceDetails;
        _combinedPhoto = _localPhoto + _azurePhoto;
        _combinedGeocode = _localGeocode + _azureGeocode;

        _logger.LogDebug(
            "Quota metrics updated — combined: nearby={Nearby}, details={Details}, photo={Photo}, geocode={Geocode}",
            _combinedNearbySearch, _combinedPlaceDetails, _combinedPhoto, _combinedGeocode);
    }
}
