using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Shared.Services;

public class LocationService : IDisposable
{
    private readonly ILogger<LocationService> _logger;
    private readonly ILocationProvider _locationProvider;
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

    public LocationService(ILogger<LocationService> logger, ILocationProvider locationProvider)
    {
        _logger = logger;
        _locationProvider = locationProvider;
    }

    public void StartTracking()
    {
        if (_timer != null) return;

        _logger.LogInformation("Starting location tracking");

        // Initial fetch immediately, then every 3 minutes
        _ = UpdateLocationAsync();
        _timer = new Timer(
            async _ => await UpdateLocationAsync(),
            null,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromMinutes(3));
    }

    private async Task UpdateLocationAsync()
    {
        try
        {
            var (lat, lng) = await _locationProvider.GetLocationAsync();

            // Only fire LocationChanged if the position actually moved (>~100m)
            var moved = !HasLocation || Math.Abs(lat - Latitude) > 0.001 || Math.Abs(lng - Longitude) > 0.001;

            Latitude = lat;
            Longitude = lng;
            HasLocation = true;
            LastError = null;

            if (moved)
            {
                _logger.LogInformation("Location updated (moved): {Lat}, {Lng}", Latitude, Longitude);
                LocationChanged?.Invoke();
            }
            else
            {
                _logger.LogDebug("Location unchanged: {Lat}, {Lng} — skipping LocationChanged", Latitude, Longitude);
            }
        }
        catch (Exception ex)
        {
            var wasLocated = HasLocation;
            Latitude = 0;
            Longitude = 0;
            HasLocation = false;
            LastError = ex.Message;
            _logger.LogWarning(ex, "Location failed: wasLocated={WasLocated}, firingChanged={Firing}",
                wasLocated, wasLocated);
            if (wasLocated)
            {
                LocationChanged?.Invoke();
            }
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
