using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivitiesApp.Data;

public sealed class EfLocalActivityStore : ILocalActivityStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EfLocalActivityStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<LocalActivity>> ListActivitiesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.Activities.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocalActivity>> ListPendingActivitiesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.Activities
            .AsNoTracking()
            .Where(a => a.SyncState != SyncState.Synced)
            .ToListAsync(cancellationToken);
    }

    public async Task<LocalActivity?> GetActivityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task SaveActivityAsync(LocalActivity activity, CancellationToken cancellationToken = default)
    {
        await SaveActivitiesAsync([activity], cancellationToken);
    }

    public async Task SaveActivitiesAsync(IEnumerable<LocalActivity> activities, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        foreach (var activity in activities)
        {
            var existing = await db.Activities.FindAsync([activity.Id], cancellationToken);
            if (existing == null)
            {
                db.Activities.Add(Clone(activity));
                continue;
            }

            Copy(existing, activity);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLastSyncTimestampAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var syncMeta = await db.SyncMetadata.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        return syncMeta?.LastSyncTimestamp;
    }

    public async Task SetLastSyncTimestampAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var syncMeta = await db.SyncMetadata.FindAsync([1], cancellationToken);

        if (syncMeta == null)
        {
            db.SyncMetadata.Add(new SyncMetadata { Id = 1, LastSyncTimestamp = timestamp });
        }
        else
        {
            syncMeta.LastSyncTimestamp = timestamp;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static LocalActivity Clone(LocalActivity source)
    {
        var clone = new LocalActivity();
        Copy(clone, source);
        return clone;
    }

    private static void Copy(LocalActivity target, LocalActivity source)
    {
        target.Id = source.Id;
        target.City = source.City;
        target.Name = source.Name;
        target.Description = source.Description;
        target.Cost = source.Cost;
        target.Activitytime = source.Activitytime;
        target.Latitude = source.Latitude;
        target.Longitude = source.Longitude;
        target.MinAge = source.MinAge;
        target.MaxAge = source.MaxAge;
        target.Category = source.Category;
        target.ImageUrl = source.ImageUrl;
        target.PlaceId = source.PlaceId;
        target.Rating = source.Rating;
        target.UpdatedAt = source.UpdatedAt;
        target.IsDeleted = source.IsDeleted;
        target.SyncState = source.SyncState;
    }
}
