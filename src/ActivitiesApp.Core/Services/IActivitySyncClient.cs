namespace ActivitiesApp.Services;

public interface IActivitySyncClient
{
    Task<PushChangesResult> PushChangesAsync(IReadOnlyCollection<ActivitySyncRecord> items, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ActivitySyncRecord> PullChangesAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
