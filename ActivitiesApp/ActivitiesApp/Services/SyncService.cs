using ActivitiesApp.Data;
using ActivitiesApp.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Services;

public class SyncService : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActivityService.ActivityServiceClient _grpcClient;
    private readonly ActivityCacheService _cache;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<SyncService> _logger;
    private Timer? _periodicTimer;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public event Action? DataChanged;

    public SyncService(
        IServiceScopeFactory scopeFactory,
        ActivityService.ActivityServiceClient grpcClient,
        ActivityCacheService cache,
        IConnectivity connectivity,
        ILogger<SyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _grpcClient = grpcClient;
        _cache = cache;
        _connectivity = connectivity;
        _logger = logger;
    }

    public void StartAutoSync()
    {
        _connectivity.ConnectivityChanged += OnConnectivityChanged;

        // Periodic sync every 5 minutes
        _periodicTimer = new Timer(
            async _ => await SyncAsync(),
            null,
            TimeSpan.Zero, // Initial sync immediately
            TimeSpan.FromMinutes(5));
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            _logger.LogInformation("Connectivity restored, triggering sync");
            await SyncAsync();
        }
    }

    public async Task SyncAsync()
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            _logger.LogDebug("No internet, skipping sync");
            return;
        }

        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("Sync already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting sync");

            await PushLocalChangesAsync();
            await PullRemoteChangesAsync();

            _logger.LogInformation("Sync completed, refreshing cache");
            await _cache.LoadFromDbAsync();
            DataChanged?.Invoke();
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Sync failed (server unreachable or RPC error)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected sync error");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task PushLocalChangesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var pendingItems = await db.Activities
            .Where(a => a.SyncState != SyncState.Synced)
            .ToListAsync();

        if (pendingItems.Count == 0)
            return;

        _logger.LogInformation("Pushing {Count} local changes", pendingItems.Count);

        using var call = _grpcClient.PushChanges();

        foreach (var item in pendingItems)
        {
            var syncItem = ToSyncItem(item);
            await call.RequestStream.WriteAsync(syncItem);
        }

        await call.RequestStream.CompleteAsync();
        var response = await call;

        _logger.LogInformation("Push result: {Created} created, {Updated} updated, {Conflicts} conflicts",
            response.CreatedCount, response.UpdatedCount, response.ConflictCount);

        // Update local items with server-resolved versions
        foreach (var resolved in response.ResolvedItems)
        {
            if (!Guid.TryParse(resolved.Id, out var id))
                continue;

            var local = await db.Activities.FindAsync(id);
            if (local == null)
                continue;

            ApplySyncItemToLocal(resolved, local);
            local.SyncState = SyncState.Synced;
        }

        await db.SaveChangesAsync();
    }

    private async Task PullRemoteChangesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var syncMeta = await db.SyncMetadata.FindAsync(1);
        var since = syncMeta?.LastSyncTimestamp ?? DateTimeOffset.MinValue;

        _logger.LogInformation("Pulling changes since {Since}", since);

        var request = new PullChangesRequest
        {
            Since = Timestamp.FromDateTimeOffset(since)
        };

        var pullCount = 0;
        var maxUpdatedAt = since;

        using var call = _grpcClient.PullChanges(request);
        await foreach (var item in call.ResponseStream.ReadAllAsync())
        {
            if (!Guid.TryParse(item.Id, out var id))
                continue;

            var local = await db.Activities.FindAsync(id);

            if (local != null)
            {
                // Don't overwrite pending local changes
                if (local.SyncState != SyncState.Synced)
                    continue;

                ApplySyncItemToLocal(item, local);
            }
            else
            {
                local = new LocalActivity { Id = id };
                ApplySyncItemToLocal(item, local);
                local.SyncState = SyncState.Synced;
                db.Activities.Add(local);
            }

            var itemUpdatedAt = item.UpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;
            if (itemUpdatedAt > maxUpdatedAt)
                maxUpdatedAt = itemUpdatedAt;

            pullCount++;
        }

        // Update sync timestamp
        if (syncMeta == null)
        {
            syncMeta = new SyncMetadata { Id = 1, LastSyncTimestamp = maxUpdatedAt };
            db.SyncMetadata.Add(syncMeta);
        }
        else
        {
            syncMeta.LastSyncTimestamp = maxUpdatedAt;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Pulled {Count} changes, new watermark: {Watermark}", pullCount, maxUpdatedAt);
    }

    private static ActivitySyncItem ToSyncItem(LocalActivity local)
    {
        return new ActivitySyncItem
        {
            Id = local.Id.ToString(),
            Name = local.Name ?? "",
            City = local.City ?? "",
            Description = local.Description ?? "",
            Cost = local.Cost,
            ActivityTime = Timestamp.FromDateTime(DateTime.SpecifyKind(local.Activitytime, DateTimeKind.Utc)),
            Latitude = local.Latitude,
            Longitude = local.Longitude,
            MinAge = local.MinAge,
            MaxAge = local.MaxAge,
            Category = local.Category ?? "",
            ImageUrl = local.ImageUrl ?? "",
            PlaceId = local.PlaceId ?? "",
            Rating = local.Rating,
            IsDeleted = local.IsDeleted || local.SyncState == SyncState.PendingDelete,
            UpdatedAt = Timestamp.FromDateTimeOffset(local.UpdatedAt)
        };
    }

    private static void ApplySyncItemToLocal(ActivitySyncItem item, LocalActivity local)
    {
        local.Name = item.Name;
        local.City = item.City;
        local.Description = item.Description;
        local.Cost = item.Cost;
        local.Activitytime = item.ActivityTime?.ToDateTime() ?? DateTime.UtcNow;
        local.Latitude = item.Latitude;
        local.Longitude = item.Longitude;
        local.MinAge = item.MinAge;
        local.MaxAge = item.MaxAge;
        local.Category = string.IsNullOrEmpty(item.Category) ? null : item.Category;
        local.ImageUrl = string.IsNullOrEmpty(item.ImageUrl) ? null : item.ImageUrl;
        local.PlaceId = string.IsNullOrEmpty(item.PlaceId) ? null : item.PlaceId;
        local.Rating = item.Rating;
        local.IsDeleted = item.IsDeleted;
        local.UpdatedAt = item.UpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow;
        local.SyncState = SyncState.Synced;
    }

    public void Dispose()
    {
        _periodicTimer?.Dispose();
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _syncLock.Dispose();
    }
}
