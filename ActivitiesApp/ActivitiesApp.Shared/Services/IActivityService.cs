using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Shared.Services;

public interface IActivityService
{
    // Activity CRUD
    Task<Activity> CreateActivityAsync(Activity activity);
    Task<Activity?> GetActivityAsync(Guid id);
    Task<List<Activity>> ListActivitiesAsync();

    // Discover nearby — searches Google, syncs to DB, returns all
    Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters);

    // Google Maps
    Task<List<NearbyPlace>> SearchNearbyPlacesAsync(double lat, double lng, int radiusMeters, string? type = null, string? keyword = null);
    Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId);
    Task<string> ReverseGeocodeAsync(double lat, double lng);
    Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode);
}
