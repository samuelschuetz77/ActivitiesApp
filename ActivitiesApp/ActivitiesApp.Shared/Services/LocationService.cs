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

    public event Action? LocationChanged;

    public LocationService(ILogger<LocationService> logger)
    {
        _logger = logger;
    }

    public void StartTracking(IJSRuntime js)
    {
        if (_timer != null) return;

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
            Latitude = coords[0];
            Longitude = coords[1];
            HasLocation = true;
            LastError = null;
            _logger.LogDebug("Location updated: {Lat}, {Lng}", Latitude, Longitude);
            LocationChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "Failed to get location");
            LocationChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
