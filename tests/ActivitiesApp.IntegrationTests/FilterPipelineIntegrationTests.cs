using ActivitiesApp.Core.Filters;
using Xunit;

namespace ActivitiesApp.IntegrationTests;

[Collection("Postgres")]
public class FilterPipelineIntegrationTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task Pipeline_SearchPizzaNoFilters_ReturnsBothPizzaPlaces()
    {
        var dataset = await fixture.GetActivitiesAsync();
        var result = ActivityFilterService.Filter(dataset, "pizza", useSearchFilters: false, new ActivityFilterCriteria());

        var names = result.Select(a => a.Name).ToList();
        Assert.Contains("Denver Pizza Palace", names);
        Assert.Contains("Mountain Pizza Hut", names);
    }

    [Fact]
    public async Task Pipeline_SearchBar_MatchesNightlifeViaSynonym()
    {
        var dataset = await fixture.GetActivitiesAsync();
        var result = ActivityFilterService.Filter(dataset, "bar", useSearchFilters: false, new ActivityFilterCriteria());

        Assert.Contains(result, a => a.Name == "The Rusty Tap Room");
        Assert.Contains(result, a => a.Name == "Boulder Brewing Tour");
    }

    [Fact]
    public async Task Pipeline_NoSearchText_AppliesOnlyDropdownFilters()
    {
        var dataset = await fixture.GetActivitiesAsync();
        var criteria = new ActivityFilterCriteria { Cost = "free" };

        var result = ActivityFilterService.Filter(dataset, "", false, criteria);

        Assert.All(result, a => Assert.Equal(0, a.Cost));
    }

    [Fact]
    public async Task Pipeline_SearchPizzaWithCostFilter_ReturnsOnlyAffordablePizzaPlaces()
    {
        var dataset = await fixture.GetActivitiesAsync();
        var criteria = new ActivityFilterCriteria { Cost = "$" };

        var result = ActivityFilterService.Filter(dataset, "pizza", useSearchFilters: true, criteria);

        var names = result.Select(a => a.Name).ToList();
        Assert.Contains("Denver Pizza Palace", names);
        Assert.Contains("Mountain Pizza Hut", names);
        Assert.DoesNotContain("Fine Dining Steakhouse", names);
        Assert.All(result, a => Assert.True(a.Cost < 15));
    }

    [Fact]
    public async Task Pipeline_SearchOutdoorsWithLocationRadius_ReturnsOnlyNearbyMatches()
    {
        var dataset = await fixture.GetActivitiesAsync();
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
    public async Task Pipeline_SearchGymWithCategoryFilter_MatchesByNameAndCategoryBoth()
    {
        var dataset = await fixture.GetActivitiesAsync();
        var criteria = new ActivityFilterCriteria { Category = "Fitness & Sports" };

        var result = ActivityFilterService.Filter(dataset, "gym", useSearchFilters: true, criteria);

        Assert.All(result, a => Assert.Equal("Fitness & Sports", a.Category));
        Assert.Contains(result, a => a.Name == "City Fitness Gym");
    }

    [Fact]
    public async Task Pipeline_FilterAndSortByTag_Outdoors_SortsNearestFirst()
    {
        var dataset = await fixture.GetActivitiesAsync();

        var result = ActivityFilterService.FilterAndSortByTag(dataset, "Outdoors", lat: 39.74, lng: -104.99);

        Assert.Equal(3, result.Count);
        Assert.Equal("Riverfront Park",       result[0].Name);
        Assert.Equal("Boulder Trail Network", result[1].Name);
        Assert.Equal("Garden of the Gods",    result[2].Name);
    }

    [Fact]
    public async Task Pipeline_FilterAndSortByTag_NoLocation_SortsByRatingDescending()
    {
        var dataset = await fixture.GetActivitiesAsync();

        var result = ActivityFilterService.FilterAndSortByTag(dataset, "Outdoors");

        Assert.Equal(4.9, result[0].Rating);
        Assert.Equal(4.9, result[1].Rating);
        Assert.Equal(4.5, result[2].Rating);
    }
}
