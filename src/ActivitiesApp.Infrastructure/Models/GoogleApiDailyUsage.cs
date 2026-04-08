namespace ActivitiesApp.Infrastructure.Models;

public class GoogleApiDailyUsage
{
    public string Id { get; set; } = "";
    public DateOnly UsageDate { get; set; }
    public string ApiType { get; set; } = "";
    public int RequestCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
