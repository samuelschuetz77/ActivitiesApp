using ActivitiesApp.Core.Helpers;
using ActivitiesApp.Core.Search;
using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Core.Filters;

public static class ActivityFilterService
{
    /// <summary>
    /// Applies fuzzy search and/or dropdown filters to a list of activities.
    /// When searchText is provided and useSearchFilters is false, only fuzzy search is applied.
    /// </summary>
    public static List<Activity> Filter(List<Activity> activities, string searchText, bool useSearchFilters, ActivityFilterCriteria criteria)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return ApplyDropdownFilters(activities, criteria).ToList();

        var scored = FuzzySearchService.Search(activities, searchText.Trim());
        var query = scored.Select(s => s.Activity);
        return (useSearchFilters ? ApplyDropdownFilters(query, criteria) : query).ToList();
    }

    /// <summary>
    /// Filters activities by tag, then sorts by distance (if location provided) then rating.
    /// </summary>
    public static List<Activity> FilterAndSortByTag(IEnumerable<Activity> activities, string tagName, double? lat = null, double? lng = null)
    {
        var matching = activities.Where(a => CategoryHelper.HasTag(a.Category, tagName));
        if (lat.HasValue && lng.HasValue)
            return matching
                .OrderBy(a => GeoCalculator.HaversineDistance(lat.Value, lng.Value, a.Latitude, a.Longitude))
                .ThenByDescending(a => a.Rating)
                .ToList();
        return matching.OrderByDescending(a => a.Rating).ToList();
    }

    public static IEnumerable<Activity> ApplyDropdownFilters(IEnumerable<Activity> query, ActivityFilterCriteria criteria)
    {
        if (!string.IsNullOrEmpty(criteria.Cost))
        {
            query = criteria.Cost switch
            {
                "free" => query.Where(a => a.Cost == 0),
                "$"    => query.Where(a => a.Cost >= 0 && a.Cost < 15),
                "$$"   => query.Where(a => a.Cost >= 15 && a.Cost <= 50),
                "$$$"  => query.Where(a => a.Cost > 50),
                _      => query
            };
        }

        if (!string.IsNullOrEmpty(criteria.Category))
        {
            query = query.Where(a => CategoryHelper.HasTag(a.Category, criteria.Category));
        }

        if (criteria.HasActiveLocation)
        {
            query = query.Where(a =>
                GeoCalculator.HaversineDistance(criteria.ActiveLatitude, criteria.ActiveLongitude, a.Latitude, a.Longitude)
                    <= criteria.RadiusMiles);
        }

        if (!string.IsNullOrEmpty(criteria.AgeRange))
        {
            var parts = criteria.AgeRange.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var minAge) && int.TryParse(parts[1], out var maxAge))
            {
                query = query.Where(a => a.MinAge <= maxAge && a.MaxAge >= minAge);
            }
        }

        return query;
    }
}
