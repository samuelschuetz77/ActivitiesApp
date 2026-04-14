namespace ActivitiesApp.Services;

public class ActivitySyncRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Description { get; set; } = "";
    public double Cost { get; set; }
    public DateTime ActivityTimeUtc { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public string Category { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string PlaceId { get; set; } = "";
    public double Rating { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Version { get; set; } = "";
}
