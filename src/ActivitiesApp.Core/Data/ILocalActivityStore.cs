namespace ActivitiesApp.Data;

public interface ILocalActivityStore
{
    Task<IReadOnlyList<LocalActivity>> ListActivitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalActivity>> ListPendingActivitiesAsync(CancellationToken cancellationToken = default);
    Task<LocalActivity?> GetActivityAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveActivityAsync(LocalActivity activity, CancellationToken cancellationToken = default);
    Task SaveActivitiesAsync(IEnumerable<LocalActivity> activities, CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> GetLastSyncTimestampAsync(CancellationToken cancellationToken = default);
    Task SetLastSyncTimestampAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default);
}
