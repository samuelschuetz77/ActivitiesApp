using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ActivitiesApp.IntegrationTests;

[Collection("Postgres")]
public class ActivityRepositoryIntegrationTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task Insert_RoundTripsAllColumns()
    {
        using var db = fixture.CreateContext();
        var activity = new Activity
        {
            Name = "Test Hike",
            City = "Denver",
            Description = "A test hike",
            Category = "Outdoors",
            Cost = 15.0,
            MinAge = 10,
            MaxAge = 11, //change back to 50 after failed test 
            Latitude = 39.74,
            Longitude = -104.99,
            Rating = 4.5,
            Activitytime = new DateTime(2026, 6, 1, 10, 0, 0),
        };

        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        var saved = await db.Activities.AsNoTracking().SingleAsync(a => a.Id == activity.Id);
        Assert.Equal("Test Hike", saved.Name);
        Assert.Equal("Denver", saved.City);
        Assert.Equal("Outdoors", saved.Category);
        Assert.Equal(15.0, saved.Cost);
        Assert.Equal(10, saved.MinAge);
        Assert.Equal(50, saved.MaxAge);
        Assert.Equal(39.74, saved.Latitude);
        Assert.Equal(-104.99, saved.Longitude);
        Assert.Equal(4.5, saved.Rating);
    }

    [Fact]
    public async Task SaveChanges_SetsUpdatedAt_OnInsert()
    {
        using var db = fixture.CreateContext();
        var before = DateTimeOffset.UtcNow;

        var activity = new Activity { Name = "Timestamp Test", City = "Denver", Description = "Test" };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        Assert.True(activity.UpdatedAt >= before);
        Assert.True(activity.UpdatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SaveChanges_UpdatesUpdatedAt_OnModify()
    {
        using var db = fixture.CreateContext();
        var activity = new Activity { Name = "Modify Test", City = "Denver", Description = "Test" };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        var firstUpdatedAt = activity.UpdatedAt;
        await Task.Delay(10);
        activity.Name = "Modify Test Updated";
        await db.SaveChangesAsync();

        Assert.True(activity.UpdatedAt >= firstUpdatedAt);
    }

    [Fact]
    public async Task Activitytime_RoundTrips_WithoutTimeZoneShift()
    {
        using var db = fixture.CreateContext();
        var localTime = new DateTime(2026, 7, 4, 14, 30, 0);

        var activity = new Activity
        {
            Name = "4th of July Fireworks",
            City = "Denver",
            Description = "Fireworks show",
            Activitytime = localTime,
        };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        await db.Entry(activity).ReloadAsync();
        Assert.Equal(localTime, activity.Activitytime);
        Assert.Equal(DateTimeKind.Unspecified, activity.Activitytime.Kind);
    }

    [Fact]
    public async Task SoftDelete_ExcludedByIsDeletedFilter()
    {
        using var db = fixture.CreateContext();
        var activity = new Activity { Name = "To Be Soft Deleted", City = "Denver", Description = "Test" };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        activity.IsDeleted = true;
        await db.SaveChangesAsync();

        var result = await db.Activities
            .Where(a => !a.IsDeleted && a.Id == activity.Id)
            .ToListAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_ByCity_ReturnsOnlyMatchingActivities()
    {
        using var db = fixture.CreateContext();
        var uniqueCity = $"TestCity-{Guid.NewGuid()}";
        db.Activities.AddRange(
            new Activity { Name = "City Event 1", City = uniqueCity, Description = "Test" },
            new Activity { Name = "City Event 2", City = uniqueCity, Description = "Test" }
        );
        await db.SaveChangesAsync();

        var results = await db.Activities
            .Where(a => a.City == uniqueCity && !a.IsDeleted)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, a => Assert.Equal(uniqueCity, a.City));
    }

    [Fact]
    public async Task GoogleApiDailyUsage_UniqueIndex_PreventsDuplicateDateAndApiType()
    {
        using var db = fixture.CreateContext();
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-999);
        db.GoogleApiDailyUsages.Add(new GoogleApiDailyUsage
        {
            Id = $"test-{date}-places",
            UsageDate = date,
            ApiType = "places",
            RequestCount = 10,
        });
        await db.SaveChangesAsync();

        db.GoogleApiDailyUsages.Add(new GoogleApiDailyUsage
        {
            Id = $"test-{date}-places-dup",
            UsageDate = date,
            ApiType = "places",
            RequestCount = 5,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
