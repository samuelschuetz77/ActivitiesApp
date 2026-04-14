using ActivitiesApp.Shared.Services;

namespace ActivitiesApp.Services;

public class MauiLocationProvider : ILocationProvider
{
    public async Task<LocationFetchResult> GetLocationAsync(CancellationToken cancellationToken = default)
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            return LocationFetchResult.Failure(
                "permission_denied",
                "Location permission was denied on this device.",
                "device",
                permissionState: status.ToString());
        }

        var location = await Geolocation.Default.GetLocationAsync(
            new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
        if (location is null)
        {
            return LocationFetchResult.Failure(
                "device_location_unavailable",
                "Unable to get device location.",
                "device",
                permissionState: status.ToString());
        }

        return LocationFetchResult.Success(
            location.Latitude,
            location.Longitude,
            "device",
            status.ToString());
    }
}
