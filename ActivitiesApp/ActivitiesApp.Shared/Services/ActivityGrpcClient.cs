using ActivitiesApp.Shared.Models;
using ActivitiesApp.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace ActivitiesApp.Shared.Services;

public class ActivityGrpcClient : IActivityService
{
    private readonly ActivityService.ActivityServiceClient _client;
    private readonly string _apiBaseAddress;

    public ActivityGrpcClient(ActivityService.ActivityServiceClient client, string apiBaseAddress)
    {
        _client = client;
        _apiBaseAddress = apiBaseAddress.TrimEnd('/');
    }

    // ─── Activity CRUD ───

    public async Task<Activity> CreateActivityAsync(Activity activity)
    {
        var request = new CreateActivityRequest
        {
            Name = activity.Name ?? "",
            City = activity.City ?? "",
            Description = activity.Description ?? "",
            Cost = activity.Cost,
            ActivityTime = Timestamp.FromDateTime(DateTime.SpecifyKind(activity.Activitytime, DateTimeKind.Utc)),
            Latitude = activity.Latitude,
            Longitude = activity.Longitude,
            MinAge = activity.MinAge,
            MaxAge = activity.MaxAge,
            Category = activity.Category ?? "",
            ImageUrl = activity.ImageUrl ?? ""
        };

        var response = await _client.CreateActivityAsync(request);
        return ToActivityModel(response);
    }

    public async Task<Activity?> GetActivityAsync(Guid id)
    {
        try
        {
            var response = await _client.GetActivityAsync(new GetActivityRequest { Id = id.ToString() });
            return ToActivityModel(response);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<Activity>> ListActivitiesAsync()
    {
        var activities = new List<Activity>();

        using var call = _client.ListActivities(new ListActivitiesRequest());
        await foreach (var response in call.ResponseStream.ReadAllAsync())
        {
            activities.Add(ToActivityModel(response));
        }

        return activities;
    }

    // ─── Discover ───

    public async Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters)
    {
        var request = new DiscoverActivitiesRequest
        {
            Latitude = lat,
            Longitude = lng,
            RadiusMeters = radiusMeters
        };

        var activities = new List<Activity>();

        using var call = _client.DiscoverActivities(request);
        await foreach (var response in call.ResponseStream.ReadAllAsync())
        {
            activities.Add(ToActivityModel(response));
        }

        return activities;
    }

    // ─── Google Maps ───

    public async Task<List<NearbyPlace>> SearchNearbyPlacesAsync(
        double lat, double lng, int radiusMeters, string? type = null, string? keyword = null)
    {
        var request = new SearchNearbyRequest
        {
            Latitude = lat,
            Longitude = lng,
            RadiusMeters = radiusMeters,
            Type = type ?? "",
            Keyword = keyword ?? ""
        };

        var places = new List<NearbyPlace>();

        using var call = _client.SearchNearbyPlaces(request);
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
                PhotoUrl = result.PhotoUrl,
                IsOpenNow = result.IsOpenNow,
                Types = result.Types_.ToList(),
                PriceLevel = result.PriceLevel
            });
        }

        return places;
    }

    public async Task<Models.PlaceDetails?> GetPlaceDetailsAsync(string placeId)
    {
        try
        {
            var response = await _client.GetPlaceDetailsAsync(
                new GetPlaceDetailsRequest { PlaceId = placeId });

            return new Models.PlaceDetails
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
        var response = await _client.ReverseGeocodeAsync(
            new ReverseGeocodeRequest { Latitude = lat, Longitude = lng });
        return response.FormattedAddress;
    }

    public async Task<ZipLookupResult?> LookupZipCodeAsync(string zipCode)
    {
        try
        {
            var response = await _client.LookupZipCodeAsync(
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

    // ─── Mapping ───

    private Activity ToActivityModel(ActivityResponse response)
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
        // Relative URLs from the API photo proxy need the API base address prepended
        if (url.StartsWith("/"))
            return _apiBaseAddress + url;
        return url;
    }
}
