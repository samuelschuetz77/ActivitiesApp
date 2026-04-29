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

    public async Task<IReadOnlyDictionary<Guid, SyncState>> GetSyncStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, SyncState>();
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.Activities
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.SyncState })
            .ToDictionaryAsync(a => a.Id, a => a.SyncState, cancellationToken);
    }

    public async Task SaveActivityAsync(LocalActivity activity, CancellationToken cancellationToken = default)
    {
        await SaveActivitiesAsync([activity], cancellationToken);
    }

    public async Task SaveActivitiesAsync(IEnumerable<LocalActivity> activities, CancellationToken cancellationToken = default)
    {
        var incoming = activities as IList<LocalActivity> ?? activities.ToList();
        if (incoming.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var ids = incoming.Select(a => a.Id).ToList();
        var existingById = await db.Activities
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (var activity in incoming)
        {
            if (existingById.TryGetValue(activity.Id, out var existing))
            {
                Copy(existing, activity);
            }
            else
            {
                db.Activities.Add(Clone(activity));
            }
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
