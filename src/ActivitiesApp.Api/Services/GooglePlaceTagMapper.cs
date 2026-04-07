namespace ActivitiesApp.Api.Services;

/// <summary>
/// Maps Google Places API type strings to user-facing activity tags.
/// A place can match multiple tags (e.g. a bar is both "Food & Drink" and "Nightlife").
/// SearchTypes are the curated Google place types we actively query for tag-specific discovery.
/// GoogleTypes are the broader types we use when assigning tags to returned places.
/// </summary>
public static class GooglePlaceTagMapper
{
    private static readonly Dictionary<string, List<string>> ReverseIndex = new();
    private static readonly Dictionary<string, TagDefinition> TagDefinitions =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TagDefinition> AllTags { get; } = BuildTags();

    private static List<TagDefinition> BuildTags()
    {
        var tags = new List<TagDefinition>
        {
            new(
                "Food & Drink",
                "utensils",
                new HashSet<string> { "restaurant", "cafe", "bakery", "bar", "meal_takeaway" },
                new HashSet<string> { "restaurant", "cafe", "bakery", "bar", "meal_delivery", "meal_takeaway", "liquor_store", "food" }),

            new(
                "Outdoors",
                "tree",
                new HashSet<string> { "park", "campground", "rv_park", "zoo" },
                new HashSet<string> { "park", "campground", "rv_park", "zoo", "natural_feature" }),

            new(
                "Arts & Culture",
                "palette",
                new HashSet<string> { "art_gallery", "museum", "library", "movie_theater" },
                new HashSet<string> { "art_gallery", "museum", "library", "movie_theater" }),

            new(
                "Nightlife",
                "moon",
                new HashSet<string> { "night_club", "bar", "casino" },
                new HashSet<string> { "night_club", "bar", "casino" }),

            new(
                "Fitness & Sports",
                "dumbbell",
                new HashSet<string> { "gym", "stadium", "bowling_alley" },
                new HashSet<string> { "gym", "stadium", "bowling_alley" }),

            new(
                "Shopping",
                "shopping-bag",
                new HashSet<string> { "shopping_mall", "clothing_store", "book_store", "electronics_store", "supermarket", "department_store" },
                new HashSet<string>
                {
                    "shopping_mall", "clothing_store", "shoe_store", "jewelry_store",
                    "department_store", "book_store", "bicycle_store", "electronics_store",
                    "furniture_store", "home_goods_store", "pet_store", "florist",
                    "convenience_store", "supermarket", "hardware_store"
                }),

            new(
                "Wellness & Beauty",
                "sparkle",
                new HashSet<string> { "spa", "beauty_salon", "hair_care" },
                new HashSet<string> { "spa", "beauty_salon", "hair_care" }),

            new(
                "Attractions",
                "star",
                new HashSet<string> { "amusement_park", "aquarium", "tourist_attraction", "zoo", "movie_theater", "bowling_alley", "casino" },
                new HashSet<string> { "amusement_park", "aquarium", "tourist_attraction", "zoo", "movie_theater", "bowling_alley", "casino" }),

            new(
                "Education",
                "graduation-cap",
                new HashSet<string> { "library", "museum", "university", "school", "book_store" },
                new HashSet<string> { "library", "museum", "university", "school", "primary_school", "secondary_school", "book_store" }),
        };

        foreach (var tag in tags)
        {
            TagDefinitions[tag.Name] = tag;

            foreach (var googleType in tag.GoogleTypes)
            {
                if (!ReverseIndex.TryGetValue(googleType, out var tagNames))
                {
                    tagNames = [];
                    ReverseIndex[googleType] = tagNames;
                }

                tagNames.Add(tag.Name);
            }
        }

        return tags;
    }

    public static List<string> GetTags(IEnumerable<string> googleTypes)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in googleTypes)
        {
            if (ReverseIndex.TryGetValue(type, out var tagNames))
            {
                foreach (var name in tagNames)
                {
                    tags.Add(name);
                }
            }
        }

        return [.. tags];
    }

    public static bool TryGetDefinition(string tagName, out TagDefinition? tagDefinition)
    {
        var found = TagDefinitions.TryGetValue(tagName, out var definition);
        tagDefinition = definition;
        return found;
    }
}

public record TagDefinition(
    string Name,
    string IconKey,
    IReadOnlySet<string> SearchTypes,
    IReadOnlySet<string> GoogleTypes);
