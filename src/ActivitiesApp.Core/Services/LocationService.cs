using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace ActivitiesApp.Shared.Services;

public class LocationService : IDisposable
{
    private readonly ILogger<LocationService> _logger;
    private readonly ILocationProvider _locationProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly List<LocationLogEntry> _recentEvents = [];
    private Timer? _timer;

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public bool HasLocation { get; private set; }
    public string? LastError { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastDiagnostics { get; private set; }
    public string? LastPermissionState { get; private set; }
    public DateTimeOffset? LastUpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptedAtUtc { get; private set; }
    public bool IsTrackingEnabled { get; private set; }
    public bool IsRefreshing { get; private set; }
    public double ManualLatitude { get; private set; }
    public double ManualLongitude { get; private set; }
    public bool HasManualLocation { get; private set; }
    public string? ManualLocationLabel { get; private set; }
    public bool HasActiveLocation => HasManualLocation || HasLocation;
    public bool IsUsingCurrentLocation => IsTrackingEnabled && HasLocation && !HasManualLocation;
    public bool IsLocationDisabled => !IsTrackingEnabled && !HasManualLocation;
    public double ActiveLatitude => HasManualLocation ? ManualLatitude : Latitude;
    public double ActiveLongitude => HasManualLocation ? ManualLongitude : Longitude;
    public IReadOnlyList<LocationLogEntry> RecentEvents => new ReadOnlyCollection<LocationLogEntry>(_recentEvents);

    public event Action? LocationChanged;

    public LocationService(ILogger<LocationService> logger, ILocationProvider locationProvider)
    {
        _logger = logger;
        _locationProvider = locationProvider;
    }

    public void StartTracking()
    {
        if (_timer == null)
        {
            _timer = new Timer(async _ => await RefreshAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        IsTrackingEnabled = true;
        _timer.Change(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
        AddEvent(LogLevel.Information, "Location tracking enabled.");
        _logger.LogInformation("Starting location tracking");
        _ = RefreshAsync();
    }

    public async Task EnableTrackingAsync()
    {
        var wasEnabled = IsTrackingEnabled;
        if (_timer == null)
        {
            _timer = new Timer(async _ => await RefreshAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        IsTrackingEnabled = true;
        _timer.Change(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
        AddEvent(LogLevel.Information, "Location tracking enabled.");
        if (!wasEnabled)
        {
            NotifyChanged();
        }
        await RefreshAsync();
    }

    public void DisableTracking()
    {
        var wasActive = HasActiveLocation;
        IsTrackingEnabled = false;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Latitude = 0;
        Longitude = 0;
        HasLocation = false;
        LastError = null;
        LastErrorCode = null;
        LastDiagnostics = null;
        LastPermissionState = null;
        AddEvent(LogLevel.Information, "Location tracking disabled.");
        _logger.LogInformation("Location tracking disabled");

        if (wasActive)
        {
            NotifyChanged();
        }
    }

    public Task RefreshAsync() => UpdateLocationAsync();

    private async Task UpdateLocationAsync()
    {
        if (!IsTrackingEnabled)
        {
            return;
        }

        if (!await _refreshLock.WaitAsync(0))
        {
            return;
        }

        var previousLat = Latitude;
        var previousLng = Longitude;
        var wasLocated = HasLocation;
        var previousError = LastError;

        try
        {
            IsRefreshing = true;
            LastAttemptedAtUtc = DateTimeOffset.UtcNow;

            var result = await _locationProvider.GetLocationAsync();
            if (!result.IsSuccess)
            {
                HandleLocationFailure(result, wasLocated, previousError);
                return;
            }

            var moved = !HasLocation ||
                        GeoMath.HaversineMeters(previousLat, previousLng, result.Latitude, result.Longitude) >= 100;

            Latitude = result.Latitude;
            Longitude = result.Longitude;
            HasLocation = true;
            LastUpdatedAtUtc = result.TimestampUtc;
            LastPermissionState = result.PermissionState;
            LastError = null;
            LastErrorCode = null;
            LastDiagnostics = null;

            AddEvent(
                LogLevel.Information,
                $"Current location updated to {Latitude:0.0000}, {Longitude:0.0000}.",
                details: $"source={result.Source}; permission={result.PermissionState ?? "unknown"}");
            _logger.LogInformation("Location updated: {Lat}, {Lng}, source={Source}, permission={PermissionState}",
                Latitude, Longitude, result.Source, result.PermissionState ?? "");

            if (moved || previousError != null)
            {
                NotifyChanged();
            }
        }
        catch (Exception ex)
        {
            HandleLocationFailure(
                LocationFetchResult.Failure("unexpected_error", ex.Message, "provider", diagnostics: ex.ToString()),
                wasLocated,
                previousError);
            _logger.LogWarning(ex, "Location failed unexpectedly: wasLocated={WasLocated}", wasLocated);
        }
        finally
        {
            IsRefreshing = false;
            _refreshLock.Release();
        }
    }

    public void SetManualLocation(double lat, double lng, string label)
    {
        ManualLatitude = lat;
        ManualLongitude = lng;
        ManualLocationLabel = label;
        HasManualLocation = true;
        LastError = null;
        LastErrorCode = null;
        LastDiagnostics = null;
        AddEvent(LogLevel.Information, $"Manual location set to {label}.", details: $"{lat:0.0000}, {lng:0.0000}");
        _logger.LogInformation("Manual location set to ({Lat},{Lng}) - {Label}", lat, lng, label);
        NotifyChanged();
    }

    public void ClearManualLocation()
    {
        HasManualLocation = false;
        ManualLatitude = 0;
        ManualLongitude = 0;
        ManualLocationLabel = null;
        AddEvent(LogLevel.Information, "Manual location cleared.");
        _logger.LogInformation("Manual location cleared, reverting to GPS");
        NotifyChanged();
    }

    private void HandleLocationFailure(LocationFetchResult result, bool wasLocated, string? previousError)
    {
        Latitude = 0;
        Longitude = 0;
        HasLocation = false;
        LastPermissionState = result.PermissionState;
        LastErrorCode = result.ErrorCode;
        LastError = result.ErrorMessage;
        LastDiagnostics = result.Diagnostics;

        AddEvent(
            LogLevel.Warning,
            result.ErrorMessage ?? "Unable to fetch current location.",
            result.ErrorCode,
            result.Diagnostics);
        _logger.LogWarning(
            "Location failed: code={Code}, message={Message}, permission={PermissionState}, source={Source}",
            result.ErrorCode ?? "", result.ErrorMessage ?? "", result.PermissionState ?? "", result.Source);

        if (wasLocated || HasManualLocation || !string.Equals(previousError, LastError, StringComparison.Ordinal))
        {
            NotifyChanged();
        }
    }

    private void AddEvent(LogLevel level, string message, string? errorCode = null, string? details = null)
    {
        _recentEvents.Insert(0, new LocationLogEntry
        {
            Level = level,
            Message = message,
            ErrorCode = errorCode,
            Details = details
        });

        if (_recentEvents.Count > 12)
        {
            _recentEvents.RemoveAt(_recentEvents.Count - 1);
        }
    }

    private void NotifyChanged()
    {
        LocationChanged?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _refreshLock.Dispose();
    }
}
