using ActivitiesApp.Data;
using ActivitiesApp.Services;
using ActivitiesApp.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActivitiesApp.Core.Tests;

public class OfflineActivityServiceTests
{
    [Fact]
    public async Task CreateActivityAsync_SavesPendingCreate_AndUpdatesCache()
    {
        var store = new InMemoryLocalActivityStore();
        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        var service = CreateService(store, cache, new FakeNetworkStatus(false), new FakeSyncClient(), _ => TestHttpMessageHandler.Json("[]"));

        var activity = new Activity
        {
            Id = Guid.NewGuid(),
            Name = "Museum",
            City = "Denver",
            Description = "Family activity"
        };

        var created = await service.CreateActivityAsync(activity);

        Assert.Equal(SyncState.PendingCreate, store.RawGet(activity.Id)!.SyncState);
        Assert.Equal(activity.Id, created.Id);
        Assert.NotNull(cache.Get(activity.Id));
    }

    [Fact]
    public async Task DiscoverActivitiesAsync_FiltersCachedResults_ByRadiusAndTag()
    {
        var store = new InMemoryLocalActivityStore();
        var near = new Activity { Id = Guid.NewGuid(), Name = "Park", City = "Denver", Description = "Near", Latitude = 39.7392, Longitude = -104.9903, Category = "outdoor,park" };
        var far = new Activity { Id = Guid.NewGuid(), Name = "Museum", City = "Boulder", Description = "Far", Latitude = 40.01499, Longitude = -105.27055, Category = "museum" };
        await store.SaveActivitiesAsync([ActivityToLocal(near), ActivityToLocal(far)]);

        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        await cache.LoadFromDbAsync();

        var service = CreateService(store, cache, new FakeNetworkStatus(false), new FakeSyncClient(), _ => TestHttpMessageHandler.Json("[]"));

        var results = await service.DiscoverActivitiesAsync(39.7392, -104.9903, 8000, "park");

        Assert.Single(results);
        Assert.Equal("Park", results[0].Name);
    }

    [Fact]
    public async Task DiscoverActivitiesAsync_BackgroundRefresh_MergesRemoteResults_AndRaisesDataChanged()
    {
        var store = new InMemoryLocalActivityStore();
        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        var network = new FakeNetworkStatus(true);
        var service = CreateService(
            store,
            cache,
            network,
            new FakeSyncClient(),
            _ => TestHttpMessageHandler.Json("""[{"id":"00000000-0000-0000-0000-000000000111","name":"Zoo","city":"Denver","description":"Animals","latitude":39.7,"longitude":-104.9,"imageUrl":"/images/zoo.png"}]"""));

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DataChanged += () => changed.TrySetResult();

        var cached = await service.DiscoverActivitiesAsync(39.7392, -104.9903, 8000);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(cached);
        var refreshed = cache.GetAll();
        Assert.Single(refreshed);
        Assert.Equal("https://api.test/images/zoo.png", refreshed[0].ImageUrl);
    }

    [Fact]
    public async Task OfflineNetworkShortCircuits_RemoteLookups()
    {
        var store = new InMemoryLocalActivityStore();
        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        var service = CreateService(store, cache, new FakeNetworkStatus(false), new FakeSyncClient(), _ => throw new InvalidOperationException("HTTP should not be called"));

        Assert.Equal("Unavailable offline", await service.ReverseGeocodeAsync(1, 2));
        Assert.Null(await service.LookupZipCodeAsync("80014"));
        Assert.Empty(await service.SearchNearbyPlacesAsync(1, 2, 1000));
    }

    private static OfflineActivityService CreateService(
        InMemoryLocalActivityStore store,
        ActivityCacheService cache,
        FakeNetworkStatus network,
        FakeSyncClient syncClient,
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new TestHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://api.test")
        };

        var syncService = new SyncService(store, syncClient, cache, network, NullLogger<SyncService>.Instance);
        return new OfflineActivityService(store, http, syncService, cache, network, NullLogger<OfflineActivityService>.Instance);
    }

    private static LocalActivity ActivityToLocal(Activity activity)
    {
        return new LocalActivity
        {
            Id = activity.Id,
            Name = activity.Name,
            City = activity.City,
            Description = activity.Description,
            Latitude = activity.Latitude,
            Longitude = activity.Longitude,
            Category = activity.Category,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
