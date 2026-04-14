namespace ActivitiesApp.Services;

public class PushChangesResult
{
    public List<ActivitySyncRecord> ResolvedItems { get; set; } = [];
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int ConflictCount { get; set; }
}
