using ActivitiesApp.Data;
using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Services;

internal static class ActivityMapping
{
    public static Activity ToActivity(LocalActivity local)
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

    public static LocalActivity ToLocalActivity(Activity activity, SyncState syncState = SyncState.Synced)
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
            IsDeleted = activity.IsDeleted,
            SyncState = syncState
        };
    }

    public static ActivitySyncRecord ToSyncRecord(LocalActivity local)
    {
        return new ActivitySyncRecord
        {
            Id = local.Id.ToString(),
            Name = local.Name ?? "",
            City = local.City ?? "",
            Description = local.Description ?? "",
            Cost = local.Cost,
            ActivityTimeUtc = DateTime.SpecifyKind(local.Activitytime, DateTimeKind.Utc),
            Latitude = local.Latitude,
            Longitude = local.Longitude,
            MinAge = local.MinAge,
            MaxAge = local.MaxAge,
            Category = local.Category ?? "",
            ImageUrl = local.ImageUrl ?? "",
            PlaceId = local.PlaceId ?? "",
            Rating = local.Rating,
            IsDeleted = local.IsDeleted || local.SyncState == SyncState.PendingDelete,
            UpdatedAt = local.UpdatedAt
        };
    }

    public static LocalActivity ToLocalActivity(ActivitySyncRecord item, SyncState syncState = SyncState.Synced)
    {
        return new LocalActivity
        {
            Id = Guid.TryParse(item.Id, out var id) ? id : Guid.NewGuid(),
            Name = item.Name,
            City = item.City,
            Description = item.Description,
            Cost = item.Cost,
            Activitytime = item.ActivityTimeUtc,
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            MinAge = item.MinAge,
            MaxAge = item.MaxAge,
            Category = string.IsNullOrEmpty(item.Category) ? null : item.Category,
            ImageUrl = string.IsNullOrEmpty(item.ImageUrl) ? null : item.ImageUrl,
            PlaceId = string.IsNullOrEmpty(item.PlaceId) ? null : item.PlaceId,
            Rating = item.Rating,
            IsDeleted = item.IsDeleted,
            UpdatedAt = item.UpdatedAt,
            SyncState = syncState
        };
    }
}
