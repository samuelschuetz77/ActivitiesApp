using ActivitiesApp.Shared.Models;
using ActivitiesApp.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Shared.Services;

public class ActivityGrpcClient : IActivityService
{
    private readonly ActivityService.ActivityServiceClient _client;
    private readonly ILogger<ActivityGrpcClient> _logger;
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(20);

    public ActivityGrpcClient(ActivityService.ActivityServiceClient client, ILogger<ActivityGrpcClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ─── Activity CRUD ───

    public async Task<Activity> CreateActivityAsync(Activity activity)
    {
        var startedAt = DateTime.UtcNow;
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

        _logger.LogInformation("CreateActivityAsync starting for {Name}", activity.Name);
        var response = await _client.CreateActivityAsync(request, deadline: startedAt.Add(DefaultDeadline));
        _logger.LogInformation("CreateActivityAsync completed for {Name} in {DurationMs}ms",
            activity.Name, (DateTime.UtcNow - startedAt).TotalMilliseconds);
        return ToActivityModel(response);
    }

    public async Task<Activity?> GetActivityAsync(Guid id)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("GetActivityAsync starting for {ActivityId}", id);
            var response = await _client.GetActivityAsync(new GetActivityRequest { Id = id.ToString() },
                deadline: startedAt.Add(DefaultDeadline));
            _logger.LogInformation("GetActivityAsync completed for {ActivityId} in {DurationMs}ms",
                id, (DateTime.UtcNow - startedAt).TotalMilliseconds);
            return ToActivityModel(response);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("GetActivityAsync did not find {ActivityId}", id);
            return null;
        }
    }

    public async Task<List<Activity>> ListActivitiesAsync()
    {
        var startedAt = DateTime.UtcNow;
        var activities = new List<Activity>();

        _logger.LogInformation("ListActivitiesAsync starting");
        using var call = _client.ListActivities(new ListActivitiesRequest(), deadline: startedAt.Add(DefaultDeadline));
        await foreach (var response in call.ResponseStream.ReadAllAsync())
        {
            activities.Add(ToActivityModel(response));
        }

        _logger.LogInformation("ListActivitiesAsync completed with {Count} activities in {DurationMs}ms",
            activities.Count, (DateTime.UtcNow - startedAt).TotalMilliseconds);
        return activities;
    }

    // ─── Discover ───

    public async Task<List<Activity>> DiscoverActivitiesAsync(double lat, double lng, int radiusMeters)
    {
        var startedAt = DateTime.UtcNow;
        var request = new DiscoverActivitiesRequest
        {
            Latitude = lat,
            Longitude = lng,
            RadiusMeters = radiusMeters
        };

        var activities = new List<Activity>();

        _logger.LogInformation(
            "DiscoverActivitiesAsync starting at ({Lat},{Lng}) radius={RadiusMeters}m",
            lat, lng, radiusMeters);
        using var call = _client.DiscoverActivities(request, deadline: startedAt.Add(DefaultDeadline));
        await foreach (var response in call.ResponseStream.ReadAllAsync())
        {
            activities.Add(ToActivityModel(response));
        }

        _logger.LogInformation(
            "DiscoverActivitiesAsync completed with {Count} activities in {DurationMs}ms",
            activities.Count, (DateTime.UtcNow - startedAt).TotalMilliseconds);
        return activities;
    }

    // ─── Google Maps ───

    public async Task<List<NearbyPlace>> SearchNearbyPlacesAsync(
        double lat, double lng, int radiusMeters, string? type = null, string? keyword = null)
    {
        var startedAt = DateTime.UtcNow;
        var request = new SearchNearbyRequest
        {
            Latitude = lat,
            Longitude = lng,
            RadiusMeters = radiusMeters,
            Type = type ?? "",
            Keyword = keyword ?? ""
        };

        var places = new List<NearbyPlace>();

        _logger.LogInformation(
            "SearchNearbyPlacesAsync starting at ({Lat},{Lng}) radius={RadiusMeters}m type={Type} keyword={Keyword}",
            lat, lng, radiusMeters, type ?? "", keyword ?? "");
        using var call = _client.SearchNearbyPlaces(request, deadline: startedAt.Add(DefaultDeadline));
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

        _logger.LogInformation("SearchNearbyPlacesAsync completed with {Count} places in {DurationMs}ms",
            places.Count, (DateTime.UtcNow - startedAt).TotalMilliseconds);
        return places;
    }

    public async Task<Models.PlaceDetails?> GetPlaceDetailsAsync(string placeId)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("GetPlaceDetailsAsync starting for {PlaceId}", placeId);
            var response = await _client.GetPlaceDetailsAsync(
                new GetPlaceDetailsRequest { PlaceId = placeId },
                deadline: startedAt.Add(DefaultDeadline));
            _logger.LogInformation("GetPlaceDetailsAsync completed for {PlaceId} in {DurationMs}ms",
                placeId, (DateTime.UtcNow - startedAt).TotalMilliseconds);

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
                PhotoUrls = response.PhotoUrls.ToList(),
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
            _logger.LogWarning("GetPlaceDetailsAsync did not find {PlaceId}", placeId);
            return null;
        }
    }

    public async Task<string> ReverseGeocodeAsync(double lat, double lng)
    {
        var startedAt = DateTime.UtcNow;
        _logger.LogInformation("ReverseGeocodeAsync starting at ({Lat},{Lng})", lat, lng);
        var response = await _client.ReverseGeocodeAsync(
            new ReverseGeocodeRequest { Latitude = lat, Longitude = lng },
            deadline: startedAt.Add(DefaultDeadline));
        _logger.LogInformation("ReverseGeocodeAsync completed in {DurationMs}ms",
            (DateTime.UtcNow - startedAt).TotalMilliseconds);
        return response.FormattedAddress;
    }

    // ─── Mapping ───

    private static Activity ToActivityModel(ActivityResponse response)
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
            ImageUrl = string.IsNullOrEmpty(response.ImageUrl) ? null : response.ImageUrl,
            PlaceId = string.IsNullOrEmpty(response.PlaceId) ? null : response.PlaceId,
            Rating = response.Rating
        };
    }
}
