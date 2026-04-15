namespace ActivitiesApp.Shared.Services;

public interface ILocationProvider
{
    Task<LocationFetchResult> GetLocationAsync(CancellationToken cancellationToken = default);
}
