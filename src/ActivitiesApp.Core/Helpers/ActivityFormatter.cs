namespace ActivitiesApp.Core.Helpers;

public static class ActivityFormatter
{
    public static string FormatCost(double cost) =>
        cost == 0 ? "Free" : $"${cost:F2}";
}
