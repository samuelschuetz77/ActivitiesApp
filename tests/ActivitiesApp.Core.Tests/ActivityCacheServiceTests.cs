using ActivitiesApp.Data;
using ActivitiesApp.Services;
using ActivitiesApp.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActivitiesApp.Core.Tests;

public class ActivityCacheServiceTests
{
    [Fact]
    public async Task LoadFromDbAsync_ExcludesDeletedRowsAndMarksLoaded()
    {
        var store = new InMemoryLocalActivityStore();
        await store.SaveActivitiesAsync(
        [
            new LocalActivity { Id = Guid.NewGuid(), Name = "Visible", City = "Denver", Description = "A", UpdatedAt = DateTimeOffset.UtcNow },
            new LocalActivity { Id = Guid.NewGuid(), Name = "Deleted", City = "Denver", Description = "B", IsDeleted = true, UpdatedAt = DateTimeOffset.UtcNow }
        ]);

        var cache = new ActivityCacheService(store, NullLogger<ActivityCacheService>.Instance);
        var changed = 0;
        cache.DataChanged += () => changed++;

        await cache.LoadFromDbAsync();

        Assert.True(cache.IsLoaded);
        Assert.Single(cache.GetAll());
        Assert.Equal(1, changed);
    }

    [Fact]
    public void AddOrUpdate_StoresAndReturnsActivity()
    {
        var cache = new ActivityCacheService(new InMemoryLocalActivityStore(), NullLogger<ActivityCacheService>.Instance);
        var activity = new Activity { Id = Guid.NewGuid(), Name = "Activity", City = "City", Description = "Desc" };

        cache.AddOrUpdate(activity);

        var fetched = cache.Get(activity.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Activity", fetched!.Name);
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void AddOrUpdate_Suppressed_DoesNotRaiseNotification()
    {
        var cache = new ActivityCacheService(new InMemoryLocalActivityStore(), NullLogger<ActivityCacheService>.Instance);
        var changed = 0;
        cache.DataChanged += () => changed++;

        cache.AddOrUpdate(new Activity { Id = Guid.NewGuid(), Name = "A", City = "C", Description = "D" }, suppressNotify: true);

        Assert.Equal(0, changed);
    }
}
