using ActivitiesApp.Data;
using ActivitiesApp.Shared.Models;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Services;

public class OfflineActivityService : IActivityService
{
    private readonly LocalDbContext _db;
    private readonly ActivityService.ActivityServiceClient _grpcClient;
    private readonly SyncService _syncService;
    private readonly ActivityCacheService _cache;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<OfflineActivityService> _logger;
    private readonly string _apiBaseAddress;

    public OfflineActivityService(
        LocalDbContext db,
        ActivityService.ActivityServiceClient grpcClient,
        SyncService syncService,
        ActivityCacheService cache,
        IConnectivity connectivity,
        ILogger<OfflineActivityService> logger,
        string apiBaseAddress)
    {
        _db = db;
        _grpcClient = grpcClient;
        _apiBaseAddress = apiBaseAddress.TrimEnd('/');
        _syncService = syncService;
        _cache = cache;
        _connectivity = connectivity;
        _logger = logger;
    }

    public event Action? DataChanged;

    // ─── Activity CRUD (offline-first, cache-backed) ───

    public async Task<Activity> CreateActivityAsync(Activity activity)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var local = new LocalActivity
        {
            Id = activity.Id,
            Name = activity.Name ?? "",
            City = activity.City ?? "",
            Description = activity.Description ?? "",
            Cost = activity.Cost,
            Activitytime = activity.Activitytime,
            Latitude = activity.Latitude,
            Longitude = activity.Longitude,
            MinAge = activity.MinAge,
            MaxAge = activity.MaxAge,
            Category = activity.Category,
            ImageUrl = activity.ImageUrl,
            PlaceId = activity.PlaceId,
            Rating = activity.Rating,
            UpdatedAt = DateTimeOffset.UtcNow,
            SyncState = SyncState.PendingCreate
        };

        _db.Activities.Add(local);
        await _db.SaveChangesAsync();

        var result = ToSharedActivity(local);
        _cache.AddOrUpdate(result);

        _logger.LogInformation("CreateActivityAsync: {Ms}ms", sw.ElapsedMilliseconds);

        // Fire-and-forget sync
        _ = _syncService.SyncAsync();

        return result;
    }

    public Task<Activity?> GetActivityAsync(Guid id)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _cache.Get(id);
        _logger.LogDebug("GetActivityAsync({Id}): {Ms}ms (cache)", id, sw.ElapsedMilliseconds);
        return Task.FromResult(result);
    }

    public Task<List<Activity>> ListActivitiesAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _cache.GetAll();
        _logger.LogDebug("ListActivitiesAsync: {Ms}ms, {Count} items (cache)", sw.ElapsedMilliseconds, result.Count);
        return Task.FromResult(result);
    }

    // ─── Discover (return cache immediately, refresh in background) ───

    public async Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Return cached data filtered by distance
        var radiusMiles = radiusMeters / 1609.34;
        var cached = _cache.GetAll()
            .Where(a => GetDistanceMiles(lat, lng, a.Latitude, a.Longitude) <= radiusMiles)
            .ToList();
        _logger.LogDebug("DiscoverActivitiesAsync: returning {Count} cached items within {Radius}mi in {Ms}ms", cached.Count, radiusMiles, sw.ElapsedMilliseconds);

        if (_connectivity.NetworkAccess == NetworkAccess.Internet)
        {
            // Background refresh from gRPC
            _ = Task.Run(async () =>
            {
                try
                {
                    var request = new DiscoverActivitiesRequest
                    {
                        Latitude = lat,
                        Longitude = lng,
                        RadiusMeters = radiusMeters
                    };

                    using var call = _grpcClient.DiscoverActivities(request);
                    await foreach (var response in call.ResponseStream.ReadAllAsync())
                    {
                        var activity = ToActivityFromResponse(response);

                        var existing = await _db.Activities.FindAsync(activity.Id);
                        if (existing == null)
                        {
                            var local = FromSharedActivity(activity);
                            local.SyncState = SyncState.Synced;
                            _db.Activities.Add(local);
                        }

                        _cache.AddOrUpdate(activity);
                    }

                    await _db.SaveChangesAsync();
                    _logger.LogInformation("DiscoverActivities background refresh complete");
                    DataChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background discover refresh failed");
                }
            });
        }

        return cached;
    }

    // ─── Google Maps (pass-through, requires online) ───

    public async Task<List<NearbyPlace>> SearchNearbyPlacesAsync(
        double lat, double lng, int radiusMeters, string? type = null, string? keyword = null)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return new List<NearbyPlace>();

        var request = new SearchNearbyRequest
        {
            Latitude = lat,
            Longitude = lng,
            RadiusMeters = radiusMeters,
            Type = type ?? "",
            Keyword = keyword ?? ""
        };

        var places = new List<NearbyPlace>();

        using var call = _grpcClient.SearchNearbyPlaces(request);
        await foreach (var result in call.ResponseStream.ReadAllAsync())
        {
            places.Add(new NearbyPlace
            {
                PlaceId = result.PlaceId,
                Name = result.Name,
                Vicinity = result.Vicinity,
                Latitude = result.Latitude,
                Longitude = result.Longitude,
                Rating = result.Rating,
                UserRatingsTotal = result.UserRatingsTotal,
                PhotoUrl = ResolveImageUrl(result.PhotoUrl) ?? "",
                IsOpenNow = result.IsOpenNow,
                Types = result.Types_.ToList(),
                PriceLevel = result.PriceLevel
            });
        }

        return places;
    }

    public async Task<PlaceDetails?> GetPlaceDetailsAsync(string placeId)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return null;

        try
        {
            var response = await _grpcClient.GetPlaceDetailsAsync(
                new GetPlaceDetailsRequest { PlaceId = placeId });

            return new PlaceDetails
            {
                PlaceId = response.PlaceId,
                Name = response.Name,
                FormattedAddress = response.FormattedAddress,
                FormattedPhone = response.FormattedPhone,
                Website = response.Website,
                Rating = response.Rating,
                UserRatingsTotal = response.UserRatingsTotal,
                Latitude = response.Latitude,
                Longitude = response.Longitude,
                PhotoUrls = response.PhotoUrls.Select(u => ResolveImageUrl(u) ?? u).ToList(),
                Reviews = response.Reviews.Select(r => new PlaceReviewData
                {
                    AuthorName = r.AuthorName,
                    Rating = r.Rating,
                    Text = r.Text,
                    RelativeTime = r.RelativeTime
                }).ToList(),
                OpeningHoursSummary = response.OpeningHoursSummary,
                PriceLevel = response.PriceLevel,
                Types = response.Types_.ToList()
            };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> ReverseGeocodeAsync(double lat, double lng)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return "Unavailable offline";

        var response = await _grpcClient.ReverseGeocodeAsync(
            new ReverseGeocodeRequest { Latitude = lat, Longitude = lng });
        return response.FormattedAddress;
    }

    public async Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return null;

        try
        {
            var response = await _grpcClient.LookupZipCodeAsync(
                new LookupZipCodeRequest { PostalCode = zipCode });

            return new ZipLookupResult
            {
                PostalCode = zipCode,
                FormattedAddress = response.FormattedAddress,
                Latitude = response.Latitude,
                Longitude = response.Longitude
            };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ZipLookupResult?> GeocodeAddressAsync(string address)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            return null;

        try
        {
            var response = await _grpcClient.GeocodeAddressAsync(
                new GeocodeAddressRequest { Address = address });

            return new ZipLookupResult
            {
                FormattedAddress = response.FormattedAddress,
                Latitude = response.Latitude,
                Longitude = response.Longitude
            };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    // ─── Mapping ───

    private static Activity ToSharedActivity(LocalActivity local)
    {
        return new Activity
        {
            Id = local.Id,
            Name = local.Name,
            City = local.City,
            Description = local.Description,
            Cost = local.Cost,
            Activitytime = local.Activitytime,
            Latitude = local.Latitude,
            Longitude = local.Longitude,
            MinAge = local.MinAge,
            MaxAge = local.MaxAge,
            Category = local.Category,
            ImageUrl = local.ImageUrl,
            PlaceId = local.PlaceId,
            Rating = local.Rating,
            UpdatedAt = local.UpdatedAt,
            IsDeleted = local.IsDeleted
        };
    }

    private static LocalActivity FromSharedActivity(Activity activity)
    {
        return new LocalActivity
        {
            Id = activity.Id,
            Name = activity.Name ?? "",
            City = activity.City ?? "",
            Description = activity.Description ?? "",
            Cost = activity.Cost,
            Activitytime = activity.Activitytime,
            Latitude = activity.Latitude,
            Longitude = activity.Longitude,
            MinAge = activity.MinAge,
            MaxAge = activity.MaxAge,
            Category = activity.Category,
            ImageUrl = activity.ImageUrl,
            PlaceId = activity.PlaceId,
            Rating = activity.Rating,
            UpdatedAt = activity.UpdatedAt,
            IsDeleted = activity.IsDeleted
        };
    }

    private Activity ToActivityFromResponse(ActivityResponse response)
    {
        return new Activity
        {
            Id = Guid.TryParse(response.Id, out var id) ? id : Guid.NewGuid(),
            Name = response.Name,
            City = response.City,
            Description = response.Description,
            Cost = response.Cost,
            Activitytime = response.ActivityTime?.ToDateTime() ?? DateTime.UtcNow,
            Latitude = response.Latitude,
            Longitude = response.Longitude,
            MinAge = response.MinAge,
            MaxAge = response.MaxAge,
            Category = string.IsNullOrEmpty(response.Category) ? null : response.Category,
            ImageUrl = ResolveImageUrl(response.ImageUrl),
            PlaceId = string.IsNullOrEmpty(response.PlaceId) ? null : response.PlaceId,
            Rating = response.Rating
        };
    }

    private string? ResolveImageUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("/"))
            return _apiBaseAddress + url;
        return url;
    }

    private static double GetDistanceMiles(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
}
