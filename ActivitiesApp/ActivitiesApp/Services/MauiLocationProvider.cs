using ActivitiesApp.Shared.Services;

namespace ActivitiesApp.Services;

public class MauiLocationProvider : ILocationProvider
{
    public async Task<(double Latitude, double Longitude)> GetLocationAsync()
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            throw new InvalidOperationException("Location permission was denied.");

        var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
        if (location is null)
            throw new InvalidOperationException("Unable to get device location.");

        return (location.Latitude, location.Longitude);
    }
}
