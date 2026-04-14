using ActivitiesApp.Protos;
using Google.Protobuf.WellKnownTypes;

namespace ActivitiesApp.Services;

public sealed class GrpcActivitySyncClient : IActivitySyncClient
{
    private readonly ActivityService.ActivityServiceClient _client;

    public GrpcActivitySyncClient(ActivityService.ActivityServiceClient client)
    {
        _client = client;
    }

    public async Task<PushChangesResult> PushChangesAsync(IReadOnlyCollection<ActivitySyncRecord> items, CancellationToken cancellationToken = default)
    {
        using var call = _client.PushChanges(cancellationToken: cancellationToken);
        foreach (var item in items)
        {
            await call.RequestStream.WriteAsync(ToProto(item), cancellationToken);
        }

        await call.RequestStream.CompleteAsync();
        var response = await call.ResponseAsync;

        return new PushChangesResult
        {
            CreatedCount = response.CreatedCount,
            UpdatedCount = response.UpdatedCount,
            ConflictCount = response.ConflictCount,
            ResolvedItems = response.ResolvedItems.Select(FromProto).ToList()
        };
    }

    public async IAsyncEnumerable<ActivitySyncRecord> PullChangesAsync(DateTimeOffset since, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var call = _client.PullChanges(new PullChangesRequest
        {
            Since = Timestamp.FromDateTimeOffset(since)
        }, cancellationToken: cancellationToken);

        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            yield return FromProto(call.ResponseStream.Current);
        }
    }

    private static ActivitySyncItem ToProto(ActivitySyncRecord item)
    {
        return new ActivitySyncItem
        {
            Id = item.Id,
            Name = item.Name,
            City = item.City,
            Description = item.Description,
            Cost = item.Cost,
            ActivityTime = Timestamp.FromDateTime(DateTime.SpecifyKind(item.ActivityTimeUtc, DateTimeKind.Utc)),
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            MinAge = item.MinAge,
            MaxAge = item.MaxAge,
            Category = item.Category,
            ImageUrl = item.ImageUrl,
            PlaceId = item.PlaceId,
            Rating = item.Rating,
            IsDeleted = item.IsDeleted,
            UpdatedAt = Timestamp.FromDateTimeOffset(item.UpdatedAt),
            Version = item.Version
        };
    }

    private static ActivitySyncRecord FromProto(ActivitySyncItem item)
    {
        return new ActivitySyncRecord
        {
            Id = item.Id,
            Name = item.Name,
            City = item.City,
            Description = item.Description,
            Cost = item.Cost,
            ActivityTimeUtc = item.ActivityTime?.ToDateTime() ?? DateTime.UtcNow,
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            MinAge = item.MinAge,
            MaxAge = item.MaxAge,
            Category = item.Category,
            ImageUrl = item.ImageUrl,
            PlaceId = item.PlaceId,
            Rating = item.Rating,
            IsDeleted = item.IsDeleted,
            UpdatedAt = item.UpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
            Version = item.Version
        };
    }
}
