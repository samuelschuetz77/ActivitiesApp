using ActivitiesApp.Data;
using ActivitiesApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActivitiesApp.Core.Tests;

public class SyncServiceTests
{
    [Fact]
    public async Task SyncAsync_WhenOffline_DoesNothing()
    {
        var store = new InMemoryLocalActivityStore();
        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        var client = new FakeSyncClient();
        var network = new FakeNetworkStatus(false);
        var service = new SyncService(store, client, cache, network, NullLogger<SyncService>.Instance);

        await service.SyncAsync();

        Assert.Empty(client.PushRequests);
    }

    [Fact]
    public async Task SyncAsync_PushesPendingItems_WithDeleteSemantics()
    {
        var store = new InMemoryLocalActivityStore();
        var activityId = Guid.NewGuid();
        await store.SaveActivityAsync(new LocalActivity
        {
            Id = activityId,
            Name = "Local",
            City = "Denver",
            Description = "Desc",
            UpdatedAt = DateTimeOffset.UtcNow,
            SyncState = SyncState.PendingDelete
        });

        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        var client = new FakeSyncClient
        {
            PushResult = new PushChangesResult
            {
                ResolvedItems =
                [
                    new ActivitySyncRecord
                    {
                        Id = activityId.ToString(),
                        Name = "Resolved",
                        City = "Denver",
                        Description = "Resolved Desc",
                        UpdatedAt = DateTimeOffset.UtcNow
                    }
                ]
            }
        };
        var service = new SyncService(store, client, cache, new FakeNetworkStatus(true), NullLogger<SyncService>.Instance);

        await service.SyncAsync();

        Assert.Single(client.PushRequests);
        Assert.True(client.PushRequests[0].IsDeleted);
        Assert.Equal(SyncState.Synced, store.RawGet(activityId)!.SyncState);
        Assert.Equal("Resolved", store.RawGet(activityId)!.Name);
    }

    [Fact]
    public async Task SyncAsync_PullDoesNotOverwritePendingLocalChanges_AndAdvancesWatermark()
    {
        var store = new InMemoryLocalActivityStore();
        var pendingId = Guid.NewGuid();
        await store.SaveActivityAsync(new LocalActivity
        {
            Id = pendingId,
            Name = "Local Pending",
            City = "Denver",
            Description = "Pending",
            SyncState = SyncState.PendingUpdate,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var incomingTimestamp = DateTimeOffset.UtcNow.AddMinutes(5);
        var client = new FakeSyncClient();
        client.PullItems.Add(new ActivitySyncRecord
        {
            Id = pendingId.ToString(),
            Name = "Remote",
            City = "Boulder",
            Description = "Remote Desc",
            UpdatedAt = incomingTimestamp
        });

        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        var service = new SyncService(store, client, cache, new FakeNetworkStatus(true), NullLogger<SyncService>.Instance);

        await service.SyncAsync();

        Assert.Equal("Local Pending", store.RawGet(pendingId)!.Name);
        Assert.Equal(incomingTimestamp, await store.GetLastSyncTimestampAsync());
    }
}
