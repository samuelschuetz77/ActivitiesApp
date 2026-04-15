namespace ActivitiesApp.Core.Helpers;

public record TagCard(string Name, string Emoji);

public static class CategoryHelper
{
    public static readonly IReadOnlyList<TagCard> TagCards =
    [
        new("Restaurant", "\ud83c\udf7d\ufe0f"),
        new("Fast Food", "\ud83c\udf54"),
        new("Convenience Store", "\ud83c\udfe0"),
        new("Attractions", "\u2b50"),
        new("Outdoors", "\ud83c\udf32"),
        new("Arts & Culture", "\ud83c\udfa8"),
        new("Nightlife", "\ud83c\udf19"),
        new("Shopping", "\ud83d\udecd\ufe0f"),
        new("Fitness & Sports", "\ud83c\udfcb\ufe0f"),
        new("Wellness & Beauty", "\u2728"),
        new("Education", "\ud83c\udf93"),
    ];

    private static readonly Dictionary<string, string> TagIconMap =
        TagCards.ToDictionary(t => t.Name, t => t.Emoji, StringComparer.OrdinalIgnoreCase);

    public static List<string> GetTagList(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return [];
        return category.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public static bool HasTag(string? category, string tagName) =>
        GetTagList(category).Any(tag => string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase));

    public static string GetFirstTag(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return "";
        var idx = category.IndexOf(',');
        return idx >= 0 ? category[..idx].Trim() : category.Trim();
    }

    public static string GetTagIcon(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return "\ud83d\udccd";
        return TagIconMap.TryGetValue(tagName, out var emoji) ? emoji : "\ud83d\udccd";
    }

    public static string GetFirstTagIcon(string? category) =>
        GetTagIcon(GetFirstTag(category));
}
