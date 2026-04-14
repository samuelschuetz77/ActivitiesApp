using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Shared.Services;

public record ScoredActivity(Activity Activity, double Score);

/// <summary>
/// Client-side fuzzy search engine with misspelling tolerance,
/// category synonym awareness, and relevance-ranked results.
/// </summary>
public static class FuzzySearchService
{
    // Weights for each field when scoring matches
    private const double NameWeight = 3.0;
    private const double CategoryWeight = 2.5;
    private const double SynonymWeight = 2.0;
    private const double CityWeight = 1.5;
    private const double DescriptionWeight = 1.0;

    // Minimum normalized score to include in results
    private const double MinScoreCutoff = 0.3;

    // Minimum fuzzy similarity to count as a match
    private const double FuzzyThreshold = 0.55;

    /// <summary>
    /// Maps common search terms and synonyms to category names.
    /// </summary>
    private static readonly Dictionary<string, List<string>> SynonymMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Restaurant — only category-level synonyms, not specific food items
        ["food"] = ["Restaurant", "Fast Food"],
        ["eat"] = ["Restaurant", "Fast Food"],
        ["eating"] = ["Restaurant", "Fast Food"],
        ["dining"] = ["Restaurant"],
        ["dine"] = ["Restaurant"],
        ["resturant"] = ["Restaurant"],
        ["restaraunt"] = ["Restaurant"],
        ["restraunt"] = ["Restaurant"],
        ["resteraunt"] = ["Restaurant"],

        // Fast Food — only category-level synonyms
        ["takeout"] = ["Fast Food"],
        ["takeaway"] = ["Fast Food"],
        ["drive-thru"] = ["Fast Food"],
        ["quick bite"] = ["Fast Food"],

        // Convenience Store
        ["grocery"] = ["Convenience Store"],
        ["groceries"] = ["Convenience Store"],
        ["supermarket"] = ["Convenience Store"],
        ["corner store"] = ["Convenience Store"],

        // Outdoors
        ["park"] = ["Outdoors"],
        ["nature"] = ["Outdoors"],
        ["hike"] = ["Outdoors"],
        ["hiking"] = ["Outdoors"],
        ["camping"] = ["Outdoors"],
        ["trail"] = ["Outdoors"],
        ["trails"] = ["Outdoors"],
        ["outdoor"] = ["Outdoors"],
        ["outdoors"] = ["Outdoors"],

        // Arts & Culture
        ["museum"] = ["Arts & Culture"],
        ["art"] = ["Arts & Culture"],
        ["gallery"] = ["Arts & Culture"],
        ["theater"] = ["Arts & Culture"],
        ["theatre"] = ["Arts & Culture"],
        ["culture"] = ["Arts & Culture"],

        // Nightlife
        ["bar"] = ["Nightlife"],
        ["bars"] = ["Nightlife"],
        ["club"] = ["Nightlife"],
        ["clubs"] = ["Nightlife"],
        ["nightclub"] = ["Nightlife"],
        ["pub"] = ["Nightlife"],
        ["pubs"] = ["Nightlife"],
        ["lounge"] = ["Nightlife"],

        // Fitness & Sports
        ["gym"] = ["Fitness & Sports"],
        ["workout"] = ["Fitness & Sports"],
        ["exercise"] = ["Fitness & Sports"],
        ["fitness"] = ["Fitness & Sports"],
        ["sports"] = ["Fitness & Sports"],
        ["sport"] = ["Fitness & Sports"],

        // Shopping
        ["shop"] = ["Shopping"],
        ["shops"] = ["Shopping"],
        ["store"] = ["Shopping", "Convenience Store"],
        ["stores"] = ["Shopping", "Convenience Store"],
        ["mall"] = ["Shopping"],
        ["retail"] = ["Shopping"],

        // Wellness & Beauty
        ["spa"] = ["Wellness & Beauty"],
        ["salon"] = ["Wellness & Beauty"],
        ["beauty"] = ["Wellness & Beauty"],
        ["wellness"] = ["Wellness & Beauty"],

        // Attractions
        ["attraction"] = ["Attractions"],
        ["attractions"] = ["Attractions"],
        ["theme park"] = ["Attractions"],
        ["amusement"] = ["Attractions"],
        ["zoo"] = ["Attractions", "Outdoors"],
        ["tourist"] = ["Attractions"],
        ["sightseeing"] = ["Attractions"],

        // Education
        ["school"] = ["Education"],
        ["learn"] = ["Education"],
        ["learning"] = ["Education"],
        ["library"] = ["Education", "Arts & Culture"],
        ["university"] = ["Education"],
        ["college"] = ["Education"],
        ["education"] = ["Education"],
    };

    /// <summary>
    /// Searches activities with fuzzy matching, synonym awareness, and relevance scoring.
    /// Returns results sorted by descending relevance score.
    /// </summary>
    public static List<ScoredActivity> Search(IEnumerable<Activity> activities, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (queryTokens.Length == 0)
            return [];

        // Resolve which categories the query maps to via synonyms
        var synonymCategories = GetSynonymCategories(normalizedQuery, queryTokens);

        var results = new List<ScoredActivity>();

        foreach (var activity in activities)
        {
            var score = ScoreActivity(activity, queryTokens, synonymCategories);
            if (score >= MinScoreCutoff)
            {
                results.Add(new ScoredActivity(activity, score));
            }
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    private static double ScoreActivity(Activity activity, string[] queryTokens, HashSet<string> synonymCategories)
    {
        double totalScore = 0;

        // Tokenize each field for matching
        var nameTokens = Tokenize(activity.Name);
        var categoryTokens = Tokenize(activity.Category);
        var cityTokens = Tokenize(activity.City);
        var descTokens = Tokenize(activity.Description);

        foreach (var qt in queryTokens)
        {
            // Best match per field
            var nameScore = BestTokenScore(qt, nameTokens);
            var categoryScore = BestTokenScore(qt, categoryTokens);
            var cityScore = BestTokenScore(qt, cityTokens);
            var descScore = BestTokenScore(qt, descTokens);

            totalScore += nameScore * NameWeight;
            totalScore += categoryScore * CategoryWeight;
            totalScore += cityScore * CityWeight;
            totalScore += descScore * DescriptionWeight;
        }

        // Synonym bonus: if the query maps to categories that this activity belongs to
        if (synonymCategories.Count > 0 && !string.IsNullOrWhiteSpace(activity.Category))
        {
            var activityTags = activity.Category
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var tag in activityTags)
            {
                if (synonymCategories.Contains(tag))
                {
                    totalScore += SynonymWeight;
                    break; // One bonus per activity, not per matching tag
                }
            }
        }

        // Normalize by token count so single-word and multi-word queries are comparable
        return totalScore / queryTokens.Length;
    }

    /// <summary>
    /// Finds the best fuzzy match score for a query token against a set of target tokens.
    /// </summary>
    private static double BestTokenScore(string queryToken, string[] targetTokens)
    {
        double best = 0;
        foreach (var tt in targetTokens)
        {
            var score = FuzzyTokenScore(queryToken, tt);
            if (score > best) best = score;
            if (best >= 1.0) break; // Can't do better than exact match
        }
        return best;
    }

    /// <summary>
    /// Computes a 0.0-1.0 similarity score between a query token and a target token.
    /// </summary>
    private static double FuzzyTokenScore(string queryToken, string targetToken)
    {
        // Exact match
        if (string.Equals(queryToken, targetToken, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        var qtLower = queryToken.ToLowerInvariant();
        var ttLower = targetToken.ToLowerInvariant();

        // Prefix match (query is prefix of target)
        if (ttLower.StartsWith(qtLower))
            return 0.9;

        // Substring match
        if (ttLower.Contains(qtLower))
            return 0.8;

        // Reverse containment (target is contained in query, e.g. query "restaurants" target "restaurant")
        if (qtLower.Contains(ttLower) && ttLower.Length >= 3)
            return 0.75;

        // Levenshtein-based fuzzy match
        var distance = LevenshteinDistance(qtLower, ttLower);
        var maxLen = Math.Max(qtLower.Length, ttLower.Length);
        if (maxLen == 0) return 0;

        var similarity = 1.0 - ((double)distance / maxLen);
        return similarity >= FuzzyThreshold ? similarity * 0.7 : 0;
    }

    /// <summary>
    /// Resolves which categories the full query and its tokens map to via the synonym dictionary.
    /// Checks both the full query string and individual tokens.
    /// </summary>
    private static HashSet<string> GetSynonymCategories(string fullQuery, string[] tokens)
    {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check full query first (handles multi-word synonyms like "theme park")
        if (SynonymMap.TryGetValue(fullQuery, out var fullMatch))
        {
            foreach (var cat in fullMatch) categories.Add(cat);
        }

        // Check individual tokens
        foreach (var token in tokens)
        {
            if (SynonymMap.TryGetValue(token, out var tokenMatch))
            {
                foreach (var cat in tokenMatch) categories.Add(cat);
            }

            // Also check fuzzy synonym matches for misspellings of synonyms
            foreach (var kvp in SynonymMap)
            {
                var synonymKey = kvp.Key;
                if (synonymKey.Contains(' ')) continue; // Skip multi-word for per-token fuzzy

                var distance = LevenshteinDistance(token.ToLowerInvariant(), synonymKey.ToLowerInvariant());
                var maxLen = Math.Max(token.Length, synonymKey.Length);
                if (maxLen > 0 && distance <= 2 && (1.0 - (double)distance / maxLen) >= FuzzyThreshold)
                {
                    foreach (var cat in kvp.Value) categories.Add(cat);
                }
            }
        }

        return categories;
    }

    /// <summary>
    /// Splits a string into lowercase tokens for matching.
    /// </summary>
    private static string[] Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.ToLowerInvariant()
            .Split([' ', ',', '&', '-', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings.
    /// Uses the two-row optimization for O(min(m,n)) space.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        // Ensure a is the shorter string for space optimization
        if (a.Length > b.Length)
            (a, b) = (b, a);

        var aLen = a.Length;
        var bLen = b.Length;

        var prevRow = new int[aLen + 1];
        var currRow = new int[aLen + 1];

        for (var i = 0; i <= aLen; i++)
            prevRow[i] = i;

        for (var j = 1; j <= bLen; j++)
        {
            currRow[0] = j;

            for (var i = 1; i <= aLen; i++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                currRow[i] = Math.Min(
                    Math.Min(currRow[i - 1] + 1, prevRow[i] + 1),
                    prevRow[i - 1] + cost);
            }

            (prevRow, currRow) = (currRow, prevRow);
        }

        return prevRow[aLen];
    }
}
