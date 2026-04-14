namespace ActivitiesApp.Shared.Services;

public interface ILocationProvider
{
    Task<(double Latitude, double Longitude)> GetLocationAsync();
}
