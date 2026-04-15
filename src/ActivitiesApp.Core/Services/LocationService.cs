using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Shared.Services;

public class LocationService : IDisposable
{
    private readonly ILogger<LocationService> _logger;
    private readonly ILocationProvider _locationProvider;
    private readonly SynchronizationContext? _syncContext;
    private Timer? _timer;
    private bool _tracking;

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public bool HasLocation { get; private set; }
    public string? LastError { get; private set; }
    public double ManualLatitude { get; private set; }
    public double ManualLongitude { get; private set; }
    public bool HasManualLocation { get; private set; }
    public string? ManualLocationLabel { get; private set; }
    public bool HasActiveLocation => HasManualLocation || HasLocation;
    public double ActiveLatitude => HasManualLocation ? ManualLatitude : Latitude;
    public double ActiveLongitude => HasManualLocation ? ManualLongitude : Longitude;

    public event Action? LocationChanged;

    public LocationService(ILogger<LocationService> logger, ILocationProvider locationProvider)
    {
        _logger = logger;
        _locationProvider = locationProvider;
        // Capture the sync context at construction time (Blazor circuit context)
        _syncContext = SynchronizationContext.Current;
    }

    public void StartTracking()
    {
        if (_tracking)
        {
            return;
        }
        _tracking = true;

        _logger.LogInformation("Starting location tracking");
        _ = UpdateLocationAsync();

        // Timer callback must marshal back to the sync context for JS interop safety.
        // In Blazor Server, JS interop can only run on the circuit's sync context.
        _timer = new Timer(_ =>
        {
            if (_syncContext != null)
            {
                _syncContext.Post(async _ =>
                {
                    try { await UpdateLocationAsync(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Timer location update failed"); }
                }, null);
            }
            else
            {
                // Non-Blazor (MAUI etc) — safe to call directly
                _ = UpdateLocationAsync();
            }
        }, null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
    }

    public Task RefreshAsync() => UpdateLocationAsync();

    private async Task UpdateLocationAsync()
    {
        try
        {
            var (lat, lng) = await _locationProvider.GetLocationAsync();
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
                _logger.LogDebug("Location unchanged: {Lat}, {Lng} - skipping LocationChanged", Latitude, Longitude);
            }
        }
        catch (Exception ex)
        {
            // Don't wipe existing good location on transient failures
            if (!HasLocation)
            {
                LastError = ex.Message;
            }
            _logger.LogWarning(ex, "Location update failed: hasLocation={HasLocation}", HasLocation);
        }
    }

    public void SetManualLocation(double lat, double lng, string label)
    {
        ManualLatitude = lat;
        ManualLongitude = lng;
        ManualLocationLabel = label;
        HasManualLocation = true;
        _logger.LogInformation("Manual location set to ({Lat},{Lng}) - {Label}", lat, lng, label);
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
