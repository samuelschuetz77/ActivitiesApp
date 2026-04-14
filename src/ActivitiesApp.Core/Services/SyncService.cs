using ActivitiesApp.Data;
using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Services;

public class SyncService : IDisposable
{
    private readonly ILocalActivityStore _store;
    private readonly IActivitySyncClient _syncClient;
    private readonly ActivityCacheService _cache;
    private readonly INetworkStatus _networkStatus;
    private readonly ILogger<SyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private Timer? _periodicTimer;

    public event Action? DataChanged;

    public SyncService(
        ILocalActivityStore store,
        IActivitySyncClient syncClient,
        ActivityCacheService cache,
        INetworkStatus networkStatus,
        ILogger<SyncService> logger)
    {
        _store = store;
        _syncClient = syncClient;
        _cache = cache;
        _networkStatus = networkStatus;
        _logger = logger;
    }

    public void StartAutoSync()
    {
        _networkStatus.ConnectivityChanged += OnConnectivityChanged;
        _periodicTimer = new Timer(async _ => await SyncAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    private async void OnConnectivityChanged(bool hasInternet)
    {
        if (hasInternet)
        {
            _logger.LogInformation("Connectivity restored, triggering sync");
            await SyncAsync();
        }
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!_networkStatus.HasInternet)
        {
            _logger.LogDebug("No internet, skipping sync");
            return;
        }

        if (!await _syncLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogDebug("Sync already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting sync");
            await PushLocalChangesAsync(cancellationToken);
            await PullRemoteChangesAsync(cancellationToken);
            _logger.LogInformation("Sync completed, refreshing cache");
            await _cache.LoadFromDbAsync(cancellationToken);
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sync failed");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task PushLocalChangesAsync(CancellationToken cancellationToken)
    {
        var pendingItems = await _store.ListPendingActivitiesAsync(cancellationToken);
        if (pendingItems.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Pushing {Count} local changes", pendingItems.Count);
        var response = await _syncClient.PushChangesAsync(pendingItems.Select(ActivityMapping.ToSyncRecord).ToList(), cancellationToken);

        _logger.LogInformation(
            "Push result: {Created} created, {Updated} updated, {Conflicts} conflicts",
            response.CreatedCount,
            response.UpdatedCount,
            response.ConflictCount);

        var resolvedLocals = response.ResolvedItems
            .Where(item => Guid.TryParse(item.Id, out _))
            .Select(item => ActivityMapping.ToLocalActivity(item, SyncState.Synced))
            .ToList();

        if (resolvedLocals.Count > 0)
        {
            await _store.SaveActivitiesAsync(resolvedLocals, cancellationToken);
        }
    }

    private async Task PullRemoteChangesAsync(CancellationToken cancellationToken)
    {
        var since = await _store.GetLastSyncTimestampAsync(cancellationToken) ?? DateTimeOffset.MinValue;
        _logger.LogInformation("Pulling changes since {Since}", since);

        var itemsToSave = new List<LocalActivity>();
        var pullCount = 0;
        var maxUpdatedAt = since;

        await foreach (var item in _syncClient.PullChangesAsync(since, cancellationToken))
        {
            if (!Guid.TryParse(item.Id, out var id))
            {
                continue;
            }

            if (item.UpdatedAt > maxUpdatedAt)
            {
                maxUpdatedAt = item.UpdatedAt;
            }

            var existing = await _store.GetActivityAsync(id, cancellationToken);
            if (existing != null && existing.SyncState != SyncState.Synced)
            {
                continue;
            }

            itemsToSave.Add(ActivityMapping.ToLocalActivity(item, SyncState.Synced));

            pullCount++;
        }

        if (itemsToSave.Count > 0)
        {
            await _store.SaveActivitiesAsync(itemsToSave, cancellationToken);
        }

        await _store.SetLastSyncTimestampAsync(maxUpdatedAt, cancellationToken);
        _logger.LogInformation("Pulled {Count} changes, new watermark: {Watermark}", pullCount, maxUpdatedAt);
    }

    public void Dispose()
    {
        _periodicTimer?.Dispose();
        _networkStatus.ConnectivityChanged -= OnConnectivityChanged;
        _syncLock.Dispose();
    }
}
