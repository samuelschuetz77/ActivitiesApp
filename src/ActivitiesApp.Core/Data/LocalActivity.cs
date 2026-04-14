namespace ActivitiesApp.Data;

public enum SyncState
{
    Synced,
    PendingCreate,
    PendingUpdate,
    PendingDelete
}

public class LocalActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string City { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double Cost { get; set; }
    public DateTime Activitytime { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public string? Category { get; set; }
    public string? ImageUrl { get; set; }
    public string? PlaceId { get; set; }
    public double Rating { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public SyncState SyncState { get; set; } = SyncState.Synced;
}

public class SyncMetadata
{
    public int Id { get; set; } = 1;
    public DateTimeOffset LastSyncTimestamp { get; set; }
}
