using ActivitiesApp.Core.Search;
using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Core.Tests;

public class FuzzySearchServiceTests
{
    private static Activity Make(string name, string category, string city = "Denver", string description = "")
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category,
            City = city,
            Description = description,
            Cost = 0,
        };

    // ── Search edge cases ──────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var activities = new List<Activity> { Make("Trail Park", "Outdoors") };
        var result = FuzzySearchService.Search(activities, "");
        Assert.Empty(result);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsEmpty()
    {
        var activities = new List<Activity> { Make("Trail Park", "Outdoors") };
        var result = FuzzySearchService.Search(activities, "   ");
        Assert.Empty(result);
    }

    [Fact]
    public void Search_EmptyList_ReturnsEmpty()
    {
        var result = FuzzySearchService.Search([], "trail");
        Assert.Empty(result);
    }

    // ── Exact and near-exact matches ───────────────────────────────────────

    [Fact]
    public void Search_ExactNameMatch_IsIncluded()
    {
        var target = Make("City Park", "Outdoors");
        var other  = Make("Downtown Mall", "Shopping");
        var activities = new List<Activity> { target, other };

        var result = FuzzySearchService.Search(activities, "City Park");

        Assert.Contains(result, r => r.Activity.Name == "City Park");
    }

    [Fact]
    public void Search_ExactNameMatch_RanksHigherThanPartial()
    {
        var exact   = Make("Pizza Palace", "Restaurant");
        var partial = Make("Palace of Art", "Arts & Culture");
        var activities = new List<Activity> { partial, exact };

        var result = FuzzySearchService.Search(activities, "Pizza Palace");

        Assert.Equal("Pizza Palace", result[0].Activity.Name);
    }

    [Fact]
    public void Search_PartialNameMatch_IsIncluded()
    {
        var target = Make("Mountain Trail Loop", "Outdoors");
        var activities = new List<Activity> { target, Make("Seafood Diner", "Restaurant") };

        var result = FuzzySearchService.Search(activities, "trail");

        Assert.Contains(result, r => r.Activity.Name == "Mountain Trail Loop");
    }

    // ── Misspelling tolerance ──────────────────────────────────────────────

    [Fact]
    public void Search_MinorMisspelling_StillFindsMatch()
    {
        var target = Make("Pizza Palace", "Restaurant");
        var activities = new List<Activity> { target };

        // "Pizzaa" is one character off from "Pizza"
        var result = FuzzySearchService.Search(activities, "pizzaa");

        Assert.NotEmpty(result);
        Assert.Contains(result, r => r.Activity.Name == "Pizza Palace");
    }

    // ── Synonym awareness ──────────────────────────────────────────────────

    [Fact]
    public void Search_SynonymBar_MatchesNightlifeCategory()
    {
        var nightlifeActivity = Make("The Rusty Tap", "Nightlife");
        var outdoorsActivity  = Make("City Park",     "Outdoors");
        var activities = new List<Activity> { nightlifeActivity, outdoorsActivity };

        var result = FuzzySearchService.Search(activities, "bar");

        Assert.Contains(result, r => r.Activity.Name == "The Rusty Tap");
    }

    [Fact]
    public void Search_SynonymHike_MatchesOutdoorsCategory()
    {
        var outdoors   = Make("Green Valley",   "Outdoors");
        var restaurant = Make("Dinner Palace",  "Restaurant");
        var activities = new List<Activity> { outdoors, restaurant };

        var result = FuzzySearchService.Search(activities, "hike");

        Assert.Contains(result, r => r.Activity.Name == "Green Valley");
    }

    [Fact]
    public void Search_SynonymFood_MatchesRestaurantAndFastFood()
    {
        var restaurant = Make("Fine Dining",   "Restaurant");
        var fastFood   = Make("Burger Joint",  "Fast Food");
        var activities = new List<Activity> { restaurant, fastFood };

        var result = FuzzySearchService.Search(activities, "food");

        var names = result.Select(r => r.Activity.Name).ToList();
        Assert.Contains("Fine Dining",  names);
        Assert.Contains("Burger Joint", names);
    }

    // ── Results are ranked by descending score ─────────────────────────────

    [Fact]
    public void Search_ReturnsResultsSortedByDescendingScore()
    {
        var activities = new List<Activity>
        {
            Make("Pizza Palace",     "Restaurant"),
            Make("Italian Kitchen",  "Restaurant"),
            Make("Outdoor Trail",    "Outdoors"),
        };

        var result = FuzzySearchService.Search(activities, "pizza");

        // Pizza Palace should be first (exact name token match beats everything)
        Assert.Equal("Pizza Palace", result[0].Activity.Name);

        // Scores must be non-increasing
        for (int i = 1; i < result.Count; i++)
            Assert.True(result[i - 1].Score >= result[i].Score);
    }

    // ── Low-relevance cutoff ───────────────────────────────────────────────

    [Fact]
    public void Search_CompletelyUnrelatedActivity_IsExcluded()
    {
        var unrelated = Make("Xyz Quantum Place", "Wellness & Beauty");
        var result = FuzzySearchService.Search([unrelated], "pizza");
        Assert.Empty(result);
    }

    // ── SearchOrAll ────────────────────────────────────────────────────────

    [Fact]
    public void SearchOrAll_NullQuery_ReturnsAllActivities()
    {
        var activities = new List<Activity>
        {
            Make("Park", "Outdoors"),
            Make("Cafe", "Restaurant"),
        };

        var result = FuzzySearchService.SearchOrAll(activities, null).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SearchOrAll_NonEmptyQuery_FiltersResults()
    {
        var activities = new List<Activity>
        {
            Make("Trail Park",  "Outdoors"),
            Make("Xyz Quantum", "Shopping"),   // completely unrelated — below score cutoff
        };

        var result = FuzzySearchService.SearchOrAll(activities, "trail").ToList();

        Assert.Contains(result, a => a.Name == "Trail Park");
        Assert.DoesNotContain(result, a => a.Name == "Xyz Quantum");
    }

    // ── City field matching ────────────────────────────────────────────────

    [Fact]
    public void Search_CityNameMatch_IsIncluded()
    {
        var denverActivity = Make("Some Museum", "Arts & Culture", city: "Denver");
        var boulderActivity = Make("Another Place", "Shopping", city: "Boulder");
        var activities = new List<Activity> { denverActivity, boulderActivity };

        var result = FuzzySearchService.Search(activities, "denver");

        Assert.Contains(result, r => r.Activity.City == "Denver");
    }
}
