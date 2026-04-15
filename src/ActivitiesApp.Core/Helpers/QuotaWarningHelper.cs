using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.Core.Helpers;

public record QuotaWarning(string Level, string Message);

public static class QuotaWarningHelper
{
    public static QuotaWarning? GetWarning(QuotaStatusResponse status)
    {
        var items = new[]
        {
            ("Nearby Search", status.NearbySearch),
            ("Place Details", status.PlaceDetails),
            ("Photos",        status.Photos),
            ("Geocoding",     status.Geocoding),
        };

        var worst = items.MaxBy(x => x.Item2.Percentage);
        if (worst.Item2.Percentage >= 95)
            return new("critical", $"Daily API limit nearly reached — discovery may be limited ({worst.Item1}: {worst.Item2.Used}/{worst.Item2.Limit})");
        if (worst.Item2.Percentage >= 80)
            return new("warning", $"API usage: {worst.Item1} at {worst.Item2.Used}/{worst.Item2.Limit} today");
        return null;
    }
}
