using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Models;
using ActivitiesApp.Protos;

namespace ActivitiesApp.Api.Services;

public class ActivityGrpcService : ActivityService.ActivityServiceBase
{
    private readonly IActivityDbContext _db;
    private readonly GooglePlacesService _places;
    private readonly ILogger<ActivityGrpcService> _logger;

    public ActivityGrpcService(IActivityDbContext db, GooglePlacesService places, ILogger<ActivityGrpcService> logger)
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
        _logger.LogInformation("ListActivities started");
        var activities = await _db.Activities
            .AsNoTracking()
            .Where(a => !a.IsDeleted)
            .ToListAsync();

        _logger.LogInformation("ListActivities loaded {Count} rows from DB", activities.Count);
        foreach (var activity in activities)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await responseStream.WriteAsync(ToActivityResponse(activity));
        }

        _logger.LogInformation("ListActivities finished streaming {Count} rows", activities.Count);
    }

    // ─── Discover (Google + DB sync) ───

    public override async Task DiscoverActivities(
        DiscoverActivitiesRequest request,
        IServerStreamWriter<ActivityResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("DiscoverActivities at ({Lat},{Lng}) radius={Radius}m tag={Tag}",
            request.Latitude, request.Longitude, request.RadiusMeters, request.TagName ?? "");

        List<GooglePlacesService.NearbyPlace> places;
        try
        {
            // Single broad search to minimize API calls
            places = await _places.SearchNearbyAsync(
                request.Latitude, request.Longitude, request.RadiusMeters,
                type: null, keyword: null);

            // If a tag was requested and fewer than 5 results match, do 1 targeted search
            if (!string.IsNullOrWhiteSpace(request.TagName) &&
                GooglePlaceTagMapper.TryGetDefinition(request.TagName, out var tagDef) && tagDef != null)
            {
                var matchCount = places.Count(p => GooglePlaceTagMapper.GetTags(p.Types)
                    .Contains(request.TagName, StringComparer.OrdinalIgnoreCase));
                if (matchCount < 5)
                {
                    _logger.LogInformation("DiscoverActivities only {MatchCount} matches for tag {Tag}, doing targeted search with type={Type}",
                        matchCount, request.TagName, tagDef.PrimarySearchType);
                    var targeted = await _places.SearchNearbyAsync(
                        request.Latitude, request.Longitude, request.RadiusMeters,
                        type: tagDef.PrimarySearchType, keyword: null);
                    var deduped = places.ToDictionary(p => p.PlaceId, p => p, StringComparer.Ordinal);
                    foreach (var p in targeted)
                    {
                        if (!string.IsNullOrWhiteSpace(p.PlaceId))
                            deduped.TryAdd(p.PlaceId, p);
                    }
                    places = deduped.Values.ToList();
                }
            }

            _logger.LogInformation("DiscoverActivities received {Count} Google places", places.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DiscoverActivities Google search failed at ({Lat},{Lng}) radius={Radius}m tag={Tag}. Falling back to DB only.",
                request.Latitude, request.Longitude, request.RadiusMeters, request.TagName ?? "");
            places = [];
        }

        // Load all existing activities that came from Google into a lookup
        var existingActivities = await _db.Activities
            .Where(a => a.PlaceId != null)
            .ToListAsync();
        _logger.LogInformation("DiscoverActivities loaded {Count} existing Google-backed DB rows", existingActivities.Count);

        var existingByPlaceId = existingActivities
            .Where(a => !string.IsNullOrEmpty(a.PlaceId))
            .GroupBy(a => a.PlaceId!)
            .ToDictionary(g => g.Key, g => g.First());

        var newActivities = new List<Activity>();
        var updatedActivities = new List<Activity>();
        var streamedExistingCount = 0;
        var streamedNewCount = 0;
        var skippedUnmappedCount = 0;

        foreach (var place in places)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            if (string.IsNullOrEmpty(place.PlaceId))
                continue;

            var tags = GooglePlaceTagMapper.GetTags(place.Types);
            if (tags.Count == 0)
            {
                skippedUnmappedCount++;
                _logger.LogDebug("Skipping unmapped Google place {PlaceId} {Name} with types [{Types}]",
                    place.PlaceId, place.Name, string.Join(",", place.Types));
                continue;
            }

            // TODO: Temporary comma-separated tag storage in Category field.
            // Migrate to a proper Tags column or join table later.
            var category = string.Join(",", tags);

            if (existingByPlaceId.TryGetValue(place.PlaceId, out var existing))
            {
                if (!string.Equals(existing.Category, category, StringComparison.Ordinal))
                {
                    existing.Category = category;
                    existing.Rating = place.Rating;
                    existing.Latitude = place.Latitude;
                    existing.Longitude = place.Longitude;
                    existing.ImageUrl = !string.IsNullOrWhiteSpace(place.PhotoUrl)
                        ? place.PhotoUrl
                        : "";
                    updatedActivities.Add(existing);

                    _logger.LogInformation(
                        "Updated existing activity {ActivityId} for place {PlaceId} with tags {Tags}",
                        existing.Id, place.PlaceId, category);
                }

                await responseStream.WriteAsync(ToActivityResponse(existing));
                streamedExistingCount++;
            }
            else
            {
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
                    Category = category, // TODO: comma-separated tags, migrate to proper Tags field
                    ImageUrl = !string.IsNullOrWhiteSpace(place.PhotoUrl)
                        ? place.PhotoUrl
                        : "",
                    PlaceId = place.PlaceId,
                    Rating = place.Rating
                };

                newActivities.Add(activity);
                existingByPlaceId[place.PlaceId] = activity;
                await responseStream.WriteAsync(ToActivityResponse(activity));
                streamedNewCount++;
            }
        }

        if (newActivities.Count > 0)
        {
            _db.Activities.AddRange(newActivities);
        }

        if (newActivities.Count > 0 || updatedActivities.Count > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "DiscoverActivities saved {NewCount} new DB rows and updated {UpdatedCount} existing rows",
                newActivities.Count, updatedActivities.Count);
        }

        if (places.Count == 0)
        {
            var dbFallbackActivities = await LoadNearbyDbActivitiesAsync(
                request.Latitude, request.Longitude, request.RadiusMeters, context.CancellationToken);
            _logger.LogInformation("DiscoverActivities DB fallback loaded {Count} rows", dbFallbackActivities.Count);

            foreach (var activity in dbFallbackActivities)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    break;

                await responseStream.WriteAsync(ToActivityResponse(activity));
            }
        }

        _logger.LogInformation(
            "DiscoverActivities finished. Streamed {ExistingCount} existing, {NewCount} new, skipped {SkippedCount} unmapped, GooglePlaceCount={GooglePlaceCount}",
            streamedExistingCount, streamedNewCount, skippedUnmappedCount, places.Count);
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
        _logger.LogInformation("ReverseGeocode requested for ({Lat},{Lng})", request.Latitude, request.Longitude);
        var address = await _places.ReverseGeocodeAsync(request.Latitude, request.Longitude);
        return new ReverseGeocodeResponse { FormattedAddress = address };
    }

    public override async Task<LookupZipCodeResponse> LookupZipCode(
        LookupZipCodeRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.PostalCode))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "postal_code is required"));
        }

        _logger.LogInformation("LookupZipCode requested for postal code {PostalCode}", request.PostalCode);
        var result = await _places.GeocodePostalCodeAsync(request.PostalCode);

        if (result == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"ZIP code {request.PostalCode} not found"));
        }

        return new LookupZipCodeResponse
        {
            Latitude = result.Value.Latitude,
            Longitude = result.Value.Longitude,
            FormattedAddress = result.Value.FormattedAddress
        };
    }

    public override async Task<GeocodeAddressResponse> GeocodeAddress(
        GeocodeAddressRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Address))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "address is required"));
        }

        _logger.LogInformation("GeocodeAddress requested for {Address}", request.Address);
        var result = await _places.GeocodeAddressAsync(request.Address);

        if (result == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Address '{request.Address}' not found"));
        }

        return new GeocodeAddressResponse
        {
            Latitude = result.Value.Latitude,
            Longitude = result.Value.Longitude,
            FormattedAddress = result.Value.FormattedAddress
        };
    }

    // ─── Delta Sync ───

    public override async Task PullChanges(
        PullChangesRequest request,
        IServerStreamWriter<ActivitySyncItem> responseStream,
        ServerCallContext context)
    {
        var since = request.Since?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;

        _logger.LogInformation("PullChanges since {Since}", since);

        var changedActivities = await _db.Activities
            .Where(a => a.UpdatedAt > since)
            .ToListAsync();

        foreach (var activity in changedActivities)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await responseStream.WriteAsync(ToSyncItem(activity));
        }

        _logger.LogInformation("PullChanges returned {Count} items", changedActivities.Count);
    }

    public override async Task<PushChangesResponse> PushChanges(
        IAsyncStreamReader<ActivitySyncItem> requestStream,
        ServerCallContext context)
    {
        var response = new PushChangesResponse();
        var resolvedItems = new List<ActivitySyncItem>();

        await foreach (var item in requestStream.ReadAllAsync(context.CancellationToken))
        {
            if (!Guid.TryParse(item.Id, out var id))
                continue;

            var existing = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id);

            if (existing == null)
            {
                // New activity from client
                var activity = FromSyncItem(item);
                _db.Activities.Add(activity);
                await _db.SaveChangesAsync();
                resolvedItems.Add(ToSyncItem(activity));
                response.CreatedCount++;
            }
            else
            {
                // Server-wins conflict resolution: if server version is newer, return server version
                var clientUpdatedAt = item.UpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;

                if (clientUpdatedAt >= existing.UpdatedAt)
                {
                    // Client is newer or same — apply client changes
                    existing.Name = item.Name;
                    existing.City = item.City;
                    existing.Description = item.Description;
                    existing.Cost = item.Cost;
                    existing.Activitytime = item.ActivityTime?.ToDateTime() ?? DateTime.UtcNow;
                    existing.Latitude = item.Latitude;
                    existing.Longitude = item.Longitude;
                    existing.MinAge = item.MinAge;
                    existing.MaxAge = item.MaxAge;
                    existing.Category = string.IsNullOrEmpty(item.Category) ? null : item.Category;
                    existing.ImageUrl = string.IsNullOrEmpty(item.ImageUrl) ? null : item.ImageUrl;
                    existing.PlaceId = string.IsNullOrEmpty(item.PlaceId) ? null : item.PlaceId;
                    existing.Rating = item.Rating;
                    existing.IsDeleted = item.IsDeleted;

                    await _db.SaveChangesAsync();
                    resolvedItems.Add(ToSyncItem(existing));
                    response.UpdatedCount++;
                }
                else
                {
                    // Server is newer — return server version (server-wins)
                    resolvedItems.Add(ToSyncItem(existing));
                    response.ConflictCount++;
                }
            }
        }

        response.ResolvedItems.AddRange(resolvedItems);
        return response;
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

    private static ActivitySyncItem ToSyncItem(Activity activity)
    {
        return new ActivitySyncItem
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
            Rating = activity.Rating,
            IsDeleted = activity.IsDeleted,
            UpdatedAt = Timestamp.FromDateTimeOffset(activity.UpdatedAt),
            Version = ""
        };
    }

    private static Activity FromSyncItem(ActivitySyncItem item)
    {
        return new Activity
        {
            Id = Guid.TryParse(item.Id, out var id) ? id : Guid.NewGuid(),
            Name = item.Name,
            City = item.City,
            Description = item.Description,
            Cost = item.Cost,
            Activitytime = item.ActivityTime?.ToDateTime() ?? DateTime.UtcNow,
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            MinAge = item.MinAge,
            MaxAge = item.MaxAge,
            Category = string.IsNullOrEmpty(item.Category) ? null : item.Category,
            ImageUrl = string.IsNullOrEmpty(item.ImageUrl) ? null : item.ImageUrl,
            PlaceId = string.IsNullOrEmpty(item.PlaceId) ? null : item.PlaceId,
            Rating = item.Rating,
            IsDeleted = item.IsDeleted
        };
    }

    private async Task<List<Activity>> LoadNearbyDbActivitiesAsync(
        double latitude,
        double longitude,
        int radiusMeters,
        CancellationToken cancellationToken)
    {
        var radiusMiles = radiusMeters / 1609.34;
        var candidates = await _db.Activities
            .AsNoTracking()
            .Where(a => !a.IsDeleted)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(a => GetDistanceMiles(latitude, longitude, a.Latitude, a.Longitude) <= radiusMiles)
            .OrderBy(a => GetDistanceMiles(latitude, longitude, a.Latitude, a.Longitude))
            .Take(50)
            .ToList();
    }

    private static double GetDistanceMiles(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusMiles = 3958.8;
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMiles * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
