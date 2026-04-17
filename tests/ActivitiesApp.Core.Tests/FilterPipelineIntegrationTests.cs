using ActivitiesApp.Core.Filters;
using ActivitiesApp.Core.Search;
using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Core.Tests;

/// <summary>
/// Integration tests for the full filter pipeline:
/// FuzzySearchService → ActivityFilterService.Filter → ApplyDropdownFilters.
///
/// These tests exercise multiple real components working together end-to-end
/// with a realistic dataset. No mocks or fakes are used — all logic runs live.
/// </summary>
public class FilterPipelineIntegrationTests
{
    private static Activity Make(string name, string category, double cost,
        double lat, double lng, int minAge = 0, int maxAge = 99, double rating = 3,
        string description = "", string city = "Denver")
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
            Description = description,
            City = city,
        };

    /// <summary>
    /// Realistic dataset spanning multiple categories, price points, and locations.
    /// Denver metro area coordinates used for geographic filtering tests.
    /// </summary>
    private static List<Activity> BuildDataset() =>
    [
        // ── Downtown Denver (39.74, -104.99) ────────────────────────────
        Make("Denver Pizza Palace",    "Restaurant",     cost: 12,  lat: 39.74, lng: -104.99, rating: 4.2),
        Make("Cheap Burger Spot",      "Fast Food",      cost: 8,   lat: 39.74, lng: -105.00, rating: 3.5),
        Make("Downtown Art Museum",    "Arts & Culture", cost: 15,  lat: 39.74, lng: -104.98, rating: 4.8),
        Make("City Fitness Gym",       "Fitness & Sports", cost: 30, lat: 39.74, lng: -104.99, rating: 4.0),
        Make("The Rusty Tap Room",     "Nightlife",      cost: 0,   lat: 39.73, lng: -104.99, rating: 3.8),
        Make("Riverfront Park",        "Outdoors",       cost: 0,   lat: 39.75, lng: -104.99, rating: 4.5),
        Make("Denver Mall",            "Shopping",       cost: 0,   lat: 39.73, lng: -104.98, rating: 3.9),
        Make("Kids Science Center",    "Attractions",    cost: 18,  lat: 39.74, lng: -104.99, minAge: 5, maxAge: 14, rating: 4.7),

        // ── Boulder (~25 mi from Denver) (40.01, -105.27) ───────────────
        Make("Boulder Trail Network",  "Outdoors",       cost: 0,   lat: 40.01, lng: -105.27, rating: 4.9),
        Make("Boulder Brewing Tour",   "Nightlife",      cost: 20,  lat: 40.02, lng: -105.27, rating: 4.3),
        Make("Mountain Pizza Hut",     "Restaurant",     cost: 14,  lat: 40.01, lng: -105.28, rating: 3.6),

        // ── Colorado Springs (~65 mi from Denver) (38.83, -104.82) ──────
        Make("Garden of the Gods",     "Outdoors",       cost: 0,   lat: 38.83, lng: -104.88, rating: 4.9),
        Make("Fine Dining Steakhouse", "Restaurant",     cost: 75,  lat: 38.83, lng: -104.82, rating: 4.6),
    ];

    // ── Integration: fuzzy search alone ────────────────────────────────────

    [Fact]
    public void Pipeline_SearchPizzaNoFilters_ReturnsBothPizzaPlaces()
    {
        var dataset = BuildDataset();
        var result = ActivityFilterService.Filter(dataset, "pizza", useSearchFilters: false, new ActivityFilterCriteria());

        var names = result.Select(a => a.Name).ToList();
        Assert.Contains("Denver Pizza Palace", names);
        Assert.Contains("Mountain Pizza Hut", names);
    }

    [Fact]
    public void Pipeline_SearchBar_MatchesNightlifeViaSynonym()
    {
        var dataset = BuildDataset();
        var result = ActivityFilterService.Filter(dataset, "bar", useSearchFilters: false, new ActivityFilterCriteria());

        // "bar" is a synonym for Nightlife — both Nightlife activities must be included
        Assert.Contains(result, a => a.Name == "The Rusty Tap Room");
        Assert.Contains(result, a => a.Name == "Boulder Brewing Tour");
    }

    // ── Integration: dropdown filters alone ────────────────────────────────

    [Fact]
    public void Pipeline_NoSearchText_AppliesOnlyDropdownFilters()
    {
        var dataset = BuildDataset();
        var criteria = new ActivityFilterCriteria { Cost = "free" };

        var result = ActivityFilterService.Filter(dataset, "", false, criteria);

        Assert.All(result, a => Assert.Equal(0, a.Cost));
    }

    // ── Integration: fuzzy search + dropdown filters combined ──────────────

    [Fact]
    public void Pipeline_SearchPizzaWithCostFilter_ReturnsOnlyAffordablePizzaPlaces()
    {
        // "Denver Pizza Palace" = $12 (matches "$" = under $15)
        // "Mountain Pizza Hut"  = $14 (also matches "$")
        // "Fine Dining Steakhouse" won't match pizza query
        var dataset = BuildDataset();
        var criteria = new ActivityFilterCriteria { Cost = "$" }; // $0–$14.99

        var result = ActivityFilterService.Filter(dataset, "pizza", useSearchFilters: true, criteria);

        var names = result.Select(a => a.Name).ToList();
        Assert.Contains("Denver Pizza Palace", names);
        Assert.Contains("Mountain Pizza Hut", names);
        Assert.DoesNotContain("Fine Dining Steakhouse", names);
        Assert.All(result, a => Assert.True(a.Cost < 15));
    }

    [Fact]
    public void Pipeline_SearchOutdoorsWithLocationRadius_ReturnsOnlyNearbyMatches()
    {
        // Searching "trail" with location filter centered on Denver, radius 10 miles
        // "Riverfront Park" is right in Denver — should match
        // "Boulder Trail Network" is ~25 mi away — should be excluded by radius
        // "Garden of the Gods" is ~65 mi away — should be excluded
        var dataset = BuildDataset();
        var criteria = new ActivityFilterCriteria
        {
            HasActiveLocation = true,
            ActiveLatitude = 39.74,
            ActiveLongitude = -104.99,
            RadiusMiles = 10,
        };

        var result = ActivityFilterService.Filter(dataset, "trail", useSearchFilters: true, criteria);

        var names = result.Select(a => a.Name).ToList();
        Assert.Contains("Riverfront Park", names);
        Assert.DoesNotContain("Boulder Trail Network", names);
        Assert.DoesNotContain("Garden of the Gods", names);
    }

    [Fact]
    public void Pipeline_SearchGymWithCategoryFilter_MatchesByNameAndCategoryBoth()
    {
        // Searching "gym" with Category="Fitness & Sports" filter
        // "City Fitness Gym" matches on name and category
        // Results must be Fitness & Sports category
        var dataset = BuildDataset();
        var criteria = new ActivityFilterCriteria { Category = "Fitness & Sports" };

        var result = ActivityFilterService.Filter(dataset, "gym", useSearchFilters: true, criteria);

        Assert.All(result, a => Assert.Equal("Fitness & Sports", a.Category));
        Assert.Contains(result, a => a.Name == "City Fitness Gym");
    }

    // ── Integration: FilterAndSortByTag with real distance calculation ──────

    [Fact]
    public void Pipeline_FilterAndSortByTag_Outdoors_SortsNearestFirst()
    {
        // From Denver (39.74, -104.99), Outdoors activities sorted by distance:
        // 1. Riverfront Park — Denver, ~0 mi
        // 2. Boulder Trail Network — ~25 mi
        // 3. Garden of the Gods — ~65 mi
        var dataset = BuildDataset();

        var result = ActivityFilterService.FilterAndSortByTag(dataset, "Outdoors", lat: 39.74, lng: -104.99);

        Assert.Equal(3, result.Count);
        Assert.Equal("Riverfront Park",       result[0].Name);
        Assert.Equal("Boulder Trail Network", result[1].Name);
        Assert.Equal("Garden of the Gods",    result[2].Name);
    }

    [Fact]
    public void Pipeline_FilterAndSortByTag_NoLocation_SortsByRatingDescending()
    {
        var dataset = BuildDataset();

        var result = ActivityFilterService.FilterAndSortByTag(dataset, "Outdoors");

        // Both Boulder Trail Network and Garden of the Gods have rating 4.9
        // Riverfront Park has 4.5
        Assert.Equal(4.9, result[0].Rating);
        Assert.Equal(4.9, result[1].Rating);
        Assert.Equal(4.5, result[2].Rating);
    }
}
