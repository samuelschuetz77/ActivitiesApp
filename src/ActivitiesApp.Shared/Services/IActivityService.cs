using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Shared.Services;

public interface IActivityService
{
    // Fired when background data refresh completes (e.g. new images loaded)
    event Action? DataChanged;

    // Activity CRUD
    Task<Activity> CreateActivityAsync(Activity activity);
    Task<Activity?> GetActivityAsync(Guid id);
    Task<List<Activity>> ListActivitiesAsync();

    // Discover nearby — searches Google, syncs to DB, returns all
    Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters, string? tagName = null);

    // Google Maps
    Task<List<NearbyPlace>> SearchNearbyPlacesAsync(double lat, double lng, int radiusMeters, string? type = null, string? keyword = null);
    Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId);
    Task<string> ReverseGeocodeAsync(double lat, double lng);
    Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode);

    // Geocode a full address to coordinates
    Task<ZipLookupResult?> GeocodeAddressAsync(string address);

    // API quota status
    Task<QuotaStatusResponse?> GetQuotaStatusAsync() => Task.FromResult<QuotaStatusResponse?>(null);
}

public class CreateActivityException : Exception
{
    public CreateActivityException(string message, Exception? inner = null) : base(message, inner) { }
}
