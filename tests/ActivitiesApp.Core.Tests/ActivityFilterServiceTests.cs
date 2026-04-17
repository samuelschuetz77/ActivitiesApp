using ActivitiesApp.Core.Filters;
using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Core.Tests;

public class ActivityFilterServiceTests
{
    private static Activity Make(string name, string category, double cost = 0,
        double lat = 0, double lng = 0, int minAge = 0, int maxAge = 99, double rating = 3)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category,
            Cost = cost,
            Latitude = lat,
            Longitude = lng,
            MinAge = minAge,
            MaxAge = maxAge,
            Rating = rating,
            City = "Denver",
            Description = "desc",
        };

    // ── ApplyDropdownFilters: cost ──────────────────────────────────────────

    [Fact]
    public void ApplyDropdownFilters_CostFree_ReturnsOnlyFreeActivities()
    {
        var activities = new List<Activity>
        {
            Make("Free Park", "Outdoors", cost: 0),
            Make("Paid Museum", "Arts & Culture", cost: 15),
        };
        var criteria = new ActivityFilterCriteria { Cost = "free" };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
        Assert.Equal("Free Park", result[0].Name);
    }

    [Fact]
    public void ApplyDropdownFilters_CostDollar_ReturnsUnderFifteen()
    {
        var activities = new List<Activity>
        {
            Make("Cheap Cafe", "Restaurant", cost: 10),
            Make("Mid Place",  "Restaurant", cost: 20),
            Make("Pricey Spa", "Wellness & Beauty", cost: 60),
        };
        var criteria = new ActivityFilterCriteria { Cost = "$" };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
        Assert.Equal("Cheap Cafe", result[0].Name);
    }

    [Fact]
    public void ApplyDropdownFilters_CostDoubleDollar_ReturnsFifteenToFifty()
    {
        var activities = new List<Activity>
        {
            Make("Free Park",   "Outdoors",         cost: 0),
            Make("Mid Dinner",  "Restaurant",       cost: 30),
            Make("Big Dinner",  "Restaurant",       cost: 55),
        };
        var criteria = new ActivityFilterCriteria { Cost = "$$" };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
        Assert.Equal("Mid Dinner", result[0].Name);
    }

    [Fact]
    public void ApplyDropdownFilters_CostTripleDollar_ReturnsOverFifty()
    {
        var activities = new List<Activity>
        {
            Make("Cheap Bite",   "Fast Food",  cost: 8),
            Make("Fancy Dinner", "Restaurant", cost: 120),
        };
        var criteria = new ActivityFilterCriteria { Cost = "$$$" };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
        Assert.Equal("Fancy Dinner", result[0].Name);
    }

    // ── ApplyDropdownFilters: category ─────────────────────────────────────

    [Fact]
    public void ApplyDropdownFilters_Category_FiltersCorrectly()
    {
        var activities = new List<Activity>
        {
            Make("Trail Run",  "Outdoors"),
            Make("Pizza Place","Restaurant"),
        };
        var criteria = new ActivityFilterCriteria { Category = "Outdoors" };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
        Assert.Equal("Trail Run", result[0].Name);
    }

    [Fact]
    public void ApplyDropdownFilters_Category_IsCaseInsensitive()
    {
        var activities = new List<Activity> { Make("Trail Run", "Outdoors") };
        var criteria = new ActivityFilterCriteria { Category = "outdoors" };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
    }

    // ── ApplyDropdownFilters: age range ────────────────────────────────────

    [Fact]
    public void ApplyDropdownFilters_AgeRange_ExcludesOutOfRangeActivities()
    {
        var activities = new List<Activity>
        {
            Make("Kids Zone",  "Attractions", minAge: 0,  maxAge: 12),
            Make("Adult Bar",  "Nightlife",   minAge: 21, maxAge: 99),
            Make("Teen Club",  "Fitness & Sports", minAge: 13, maxAge: 20),
        };
        var criteria = new ActivityFilterCriteria { AgeRange = "13-20" };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
        Assert.Equal("Teen Club", result[0].Name);
    }

    // ── ApplyDropdownFilters: location radius ──────────────────────────────

    [Fact]
    public void ApplyDropdownFilters_LocationRadius_ExcludesFarActivities()
    {
        // Denver approx 39.74, -104.99
        // Boulder is ~25 miles from Denver
        var activities = new List<Activity>
        {
            Make("Downtown Gym", "Fitness & Sports", lat: 39.74, lng: -104.99),  // ~0 mi
            Make("Boulder Hike", "Outdoors",          lat: 40.01, lng: -105.27),  // ~25 mi
        };
        var criteria = new ActivityFilterCriteria
        {
            HasActiveLocation = true,
            ActiveLatitude = 39.74,
            ActiveLongitude = -104.99,
            RadiusMiles = 10,
        };

        var result = ActivityFilterService.ApplyDropdownFilters(activities, criteria).ToList();

        Assert.Single(result);
        Assert.Equal("Downtown Gym", result[0].Name);
    }

    // ── FilterAndSortByTag ─────────────────────────────────────────────────

    [Fact]
    public void FilterAndSortByTag_NoLocation_SortsByRatingDescending()
    {
        var activities = new List<Activity>
        {
            Make("Low Rated Park",  "Outdoors", rating: 2),
            Make("High Rated Park", "Outdoors", rating: 5),
            Make("Restaurant",      "Restaurant"),
        };

        var result = ActivityFilterService.FilterAndSortByTag(activities, "Outdoors");

        Assert.Equal(2, result.Count);
        Assert.Equal("High Rated Park", result[0].Name);
        Assert.Equal("Low Rated Park",  result[1].Name);
    }

    [Fact]
    public void FilterAndSortByTag_WithLocation_SortsByDistanceThenRating()
    {
        // User is at Denver (39.74, -104.99)
        var activities = new List<Activity>
        {
            Make("Far Park",  "Outdoors", lat: 40.01, lng: -105.27, rating: 5), // ~25 mi (Boulder)
            Make("Near Park", "Outdoors", lat: 39.75, lng: -105.00, rating: 2), // ~0.7 mi
        };

        var result = ActivityFilterService.FilterAndSortByTag(activities, "Outdoors", lat: 39.74, lng: -104.99);

        Assert.Equal(2, result.Count);
        Assert.Equal("Near Park", result[0].Name);
        Assert.Equal("Far Park",  result[1].Name);
    }

    // ── Filter ─────────────────────────────────────────────────────────────

    [Fact]
    public void Filter_EmptySearchText_ReturnsAllActivitiesAfterDropdownFilters()
    {
        var activities = new List<Activity>
        {
            Make("Restaurant A", "Restaurant", cost: 0),
            Make("Restaurant B", "Restaurant", cost: 20),
        };
        var criteria = new ActivityFilterCriteria { Cost = "free" };

        var result = ActivityFilterService.Filter(activities, "", false, criteria);

        Assert.Single(result);
        Assert.Equal("Restaurant A", result[0].Name);
    }

    [Fact]
    public void Filter_SearchTextOnly_IgnoresDropdownFilters()
    {
        var activities = new List<Activity>
        {
            Make("Trail Hike", "Outdoors", cost: 0),
            Make("Pizza Place", "Restaurant", cost: 100),
        };
        // With useSearchFilters=false, cost filter should NOT apply
        var criteria = new ActivityFilterCriteria { Cost = "free" };

        var result = ActivityFilterService.Filter(activities, "pizza", useSearchFilters: false, criteria);

        Assert.Single(result);
        Assert.Equal("Pizza Place", result[0].Name);
    }

    [Fact]
    public void Filter_SearchTextWithFilters_AppliesBoth()
    {
        var activities = new List<Activity>
        {
            Make("Cheap Pizza",      "Restaurant", cost: 10),
            Make("Expensive Pizza",  "Restaurant", cost: 80),
            Make("Cheap Trail",      "Outdoors",   cost: 5),
        };
        var criteria = new ActivityFilterCriteria { Cost = "$" }; // under $15

        var result = ActivityFilterService.Filter(activities, "pizza", useSearchFilters: true, criteria);

        Assert.Single(result);
        Assert.Equal("Cheap Pizza", result[0].Name);
    }
}
