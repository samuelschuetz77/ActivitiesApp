using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using ActivitiesApp.Api.Data;
using ActivitiesApp.Api.Models;
using ActivitiesApp.Protos;

namespace ActivitiesApp.Api.Services;

public class ActivityGrpcService : ActivityService.ActivityServiceBase
{
    private readonly AppDbContext _db;
    private readonly GooglePlacesService _places;
    private readonly ILogger<ActivityGrpcService> _logger;

    public ActivityGrpcService(AppDbContext db, GooglePlacesService places, ILogger<ActivityGrpcService> logger)
    {
        _db = db;
        _places = places;
        _logger = logger;
    }

    // ─── Activity CRUD ───

    public override async Task<ActivityResponse> CreateActivity(CreateActivityRequest request, ServerCallContext context)
    {
        var activity = new Activity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            City = request.City,
            Description = request.Description,
            Cost = request.Cost,
            Activitytime = request.ActivityTime?.ToDateTime() ?? DateTime.UtcNow,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            MinAge = request.MinAge,
            MaxAge = request.MaxAge,
            Category = request.Category,
            ImageUrl = request.ImageUrl
        };

        _db.Activities.Add(activity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created activity {Name} in {City}", activity.Name, activity.City);

        return ToActivityResponse(activity);
    }

    public override async Task<ActivityResponse> GetActivity(GetActivityRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid activity ID format"));
        }

        var activity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id);
        if (activity == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Activity {request.Id} not found"));
        }

        return ToActivityResponse(activity);
    }

    public override async Task ListActivities(
        ListActivitiesRequest request,
        IServerStreamWriter<ActivityResponse> responseStream,
        ServerCallContext context)
    {
        var activities = await _db.Activities.ToListAsync();

        foreach (var activity in activities)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await responseStream.WriteAsync(ToActivityResponse(activity));
        }
    }

    // ─── Discover (Google + DB sync) ───

    public override async Task DiscoverActivities(
        DiscoverActivitiesRequest request,
        IServerStreamWriter<ActivityResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("DiscoverActivities at ({Lat},{Lng}) radius={Radius}m",
            request.Latitude, request.Longitude, request.RadiusMeters);

        // Search Google for fun things nearby
        var places = await _places.SearchNearbyAsync(
            request.Latitude, request.Longitude, request.RadiusMeters,
            type: null, keyword: "fun things to do");

        // Load all existing activities that came from Google into a lookup
        var existingActivities = await _db.Activities
            .Where(a => a.PlaceId != null)
            .ToListAsync();

        var existingByPlaceId = existingActivities
            .Where(a => !string.IsNullOrEmpty(a.PlaceId))
            .GroupBy(a => a.PlaceId!)
            .ToDictionary(g => g.Key, g => g.First());

        var newActivities = new List<Activity>();

        foreach (var place in places)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            if (string.IsNullOrEmpty(place.PlaceId))
                continue;

            if (existingByPlaceId.TryGetValue(place.PlaceId, out var existing))
            {
                await responseStream.WriteAsync(ToActivityResponse(existing));
            }
            else
            {
                var category = MapGoogleTypeToCategory(place.Types);

                var activity = new Activity
                {
                    Id = Guid.NewGuid(),
                    Name = place.Name,
                    City = place.Vicinity,
                    Description = "",
                    Cost = place.PriceLevel * 15.0,
                    Activitytime = DateTime.UtcNow,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    MinAge = 0,
                    MaxAge = 99,
                    Category = category,
                    ImageUrl = place.PhotoUrl,
                    PlaceId = place.PlaceId,
                    Rating = place.Rating
                };

                newActivities.Add(activity);
                existingByPlaceId[place.PlaceId] = activity;
                await responseStream.WriteAsync(ToActivityResponse(activity));
            }
        }

        // Batch save all new activities
        if (newActivities.Count > 0)
        {
            _db.Activities.AddRange(newActivities);
            await _db.SaveChangesAsync();
        }
    }

    private static string MapGoogleTypeToCategory(List<string> types)
    {
        if (types.Any(t => t is "park" or "campground" or "natural_feature"))
            return "Outdoors";
        if (types.Any(t => t is "gym" or "stadium" or "bowling_alley"))
            return "Sports";
        if (types.Any(t => t is "art_gallery" or "museum" or "movie_theater"))
            return "Arts";
        if (types.Any(t => t is "restaurant" or "cafe" or "bakery" or "bar" or "food"))
            return "Food";
        if (types.Any(t => t is "night_club" or "concert_hall"))
            return "Music";
        if (types.Any(t => t is "library" or "book_store" or "university" or "school"))
            return "Education";
        if (types.Any(t => t is "amusement_park" or "zoo" or "aquarium" or "tourist_attraction"))
            return "Social";
        return "Social";
    }

    // ─── Google Maps ───

    public override async Task SearchNearbyPlaces(
        SearchNearbyRequest request,
        IServerStreamWriter<PlaceResult> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("SearchNearbyPlaces at ({Lat},{Lng}) radius={Radius}m type={Type}",
            request.Latitude, request.Longitude, request.RadiusMeters, request.Type);

        var places = await _places.SearchNearbyAsync(
            request.Latitude, request.Longitude, request.RadiusMeters,
            string.IsNullOrEmpty(request.Type) ? null : request.Type,
            string.IsNullOrEmpty(request.Keyword) ? null : request.Keyword);

        foreach (var place in places)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            var result = new PlaceResult
            {
                PlaceId = place.PlaceId,
                Name = place.Name,
                Vicinity = place.Vicinity,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Rating = place.Rating,
                UserRatingsTotal = place.UserRatingsTotal,
                PhotoUrl = place.PhotoUrl,
                IsOpenNow = place.IsOpenNow,
                PriceLevel = place.PriceLevel
            };
            result.Types_.AddRange(place.Types);

            await responseStream.WriteAsync(result);
        }
    }

    public override async Task<PlaceDetailsResponse> GetPlaceDetails(
        GetPlaceDetailsRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.PlaceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "place_id is required"));
        }

        _logger.LogInformation("GetPlaceDetails for {PlaceId}", request.PlaceId);

        var details = await _places.GetPlaceDetailsAsync(request.PlaceId);
        if (details == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Place {request.PlaceId} not found"));
        }

        var response = new PlaceDetailsResponse
        {
            PlaceId = details.PlaceId,
            Name = details.Name,
            FormattedAddress = details.FormattedAddress,
            FormattedPhone = details.FormattedPhone,
            Website = details.Website,
            Rating = details.Rating,
            UserRatingsTotal = details.UserRatingsTotal,
            Latitude = details.Latitude,
            Longitude = details.Longitude,
            OpeningHoursSummary = details.OpeningHoursSummary,
            PriceLevel = details.PriceLevel
        };
        response.PhotoUrls.AddRange(details.PhotoUrls);
        response.Types_.AddRange(details.Types);

        foreach (var rev in details.Reviews)
        {
            response.Reviews.Add(new Protos.PlaceReview
            {
                AuthorName = rev.AuthorName,
                Rating = rev.Rating,
                Text = rev.Text,
                RelativeTime = rev.RelativeTime
            });
        }

        return response;
    }

    public override async Task<ReverseGeocodeResponse> ReverseGeocode(
        ReverseGeocodeRequest request, ServerCallContext context)
    {
        var address = await _places.ReverseGeocodeAsync(request.Latitude, request.Longitude);
        return new ReverseGeocodeResponse { FormattedAddress = address };
    }

    // ─── Helpers ───

    private static ActivityResponse ToActivityResponse(Activity activity)
    {
        return new ActivityResponse
        {
            Id = activity.Id.ToString(),
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
            ImageUrl = activity.ImageUrl ?? "",
            PlaceId = activity.PlaceId ?? "",
            Rating = activity.Rating
        };
    }
}
