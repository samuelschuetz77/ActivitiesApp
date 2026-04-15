using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Shared.Services;

public interface IActivityService
{
    event Action? DataChanged;

    Task<Activity> CreateActivityAsync(Activity activity);
    Task<Activity?> GetActivityAsync(Guid id);
    Task<List<Activity>> ListActivitiesAsync();
    Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters, string? tagName = null);
    Task<List<Activity>> SearchActivitiesAsync(double lat, double lng, int radiusMeters, string searchTerm);
    Task<List<NearbyPlace>> SearchNearbyPlacesAsync(double lat, double lng, int radiusMeters, string? type = null, string? keyword = null);
    Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId);
    Task<string> ReverseGeocodeAsync(double lat, double lng);
    Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode);
    Task<ZipLookupResult?> GeocodeAddressAsync(string address);
    Task<QuotaStatusResponse?> GetQuotaStatusAsync() => Task.FromResult<QuotaStatusResponse?>(null);
}

public class CreateActivityException : Exception
{
    public CreateActivityException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
