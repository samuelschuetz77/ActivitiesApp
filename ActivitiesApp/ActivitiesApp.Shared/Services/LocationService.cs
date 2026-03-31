using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ActivitiesApp.Shared.Services;

public class LocationService : IDisposable
{
    private readonly ILogger<LocationService> _logger;
    private Timer? _timer;

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public bool HasLocation { get; private set; }
    public string? LastError { get; private set; }

    // Manual location override (ZIP entry) — persists across page navigation
    public double ManualLatitude { get; private set; }
    public double ManualLongitude { get; private set; }
    public bool HasManualLocation { get; private set; }
    public string? ManualLocationLabel { get; private set; }

    // Convenience: manual override takes priority over GPS
    public bool HasActiveLocation => HasManualLocation || HasLocation;
    public double ActiveLatitude => HasManualLocation ? ManualLatitude : Latitude;
    public double ActiveLongitude => HasManualLocation ? ManualLongitude : Longitude;

    public event Action? LocationChanged;

    public LocationService(ILogger<LocationService> logger)
    {
        _logger = logger;
    }

    public void StartTracking(IJSRuntime js)
    {
        if (_timer != null) return;

        _logger.LogInformation("Starting browser location tracking");

        // Initial fetch immediately, then every 3 minutes
        _ = UpdateLocationAsync(js);
        _timer = new Timer(
            async _ => await UpdateLocationAsync(js),
            null,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromMinutes(3));
    }

    private async Task UpdateLocationAsync(IJSRuntime js)
    {
        try
        {
            var coords = await js.InvokeAsync<double[]>("getUserLocation");
            if (coords.Length < 2)
            {
                throw new InvalidOperationException("Browser location response was missing coordinates.");
            }

            Latitude = coords[0];
            Longitude = coords[1];
            HasLocation = true;
            LastError = null;
            _logger.LogInformation("Location updated: {Lat}, {Lng}", Latitude, Longitude);
            LocationChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Latitude = 0;
            Longitude = 0;
            HasLocation = false;
            LastError = ex.Message;
            _logger.LogWarning(ex, "Failed to get location");
            LocationChanged?.Invoke();
        }
    }

    public void SetManualLocation(double lat, double lng, string label)
    {
        ManualLatitude = lat;
        ManualLongitude = lng;
        ManualLocationLabel = label;
        HasManualLocation = true;
        _logger.LogInformation("Manual location set to ({Lat},{Lng}) — {Label}", lat, lng, label);
        LocationChanged?.Invoke();
    }

    public void ClearManualLocation()
    {
        HasManualLocation = false;
        ManualLatitude = 0;
        ManualLongitude = 0;
        ManualLocationLabel = null;
        _logger.LogInformation("Manual location cleared, reverting to GPS");
        LocationChanged?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
