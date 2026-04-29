using ActivitiesApp.Data;
using ActivitiesApp.Services;
using ActivitiesApp.Shared.Services;
using System.Net;
using System.Net.Http;
using System.Text;

namespace ActivitiesApp.Core.Tests;

internal sealed class InMemoryLocalActivityStore : ILocalActivityStore
{
    private readonly Dictionary<Guid, LocalActivity> _activities = new();
    private DateTimeOffset? _lastSyncTimestamp;

    public Task<IReadOnlyList<LocalActivity>> ListActivitiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LocalActivity>>(_activities.Values.Select(Clone).ToList());

    public Task<IReadOnlyList<LocalActivity>> ListPendingActivitiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LocalActivity>>(_activities.Values.Where(a => a.SyncState != SyncState.Synced).Select(Clone).ToList());

    public Task<LocalActivity?> GetActivityAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_activities.TryGetValue(id, out var activity) ? Clone(activity) : null);

    public Task<IReadOnlyDictionary<Guid, SyncState>> GetSyncStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<Guid, SyncState> result = ids
            .Where(_activities.ContainsKey)
            .ToDictionary(id => id, id => _activities[id].SyncState);
        return Task.FromResult(result);
    }

    public Task SaveActivityAsync(LocalActivity activity, CancellationToken cancellationToken = default)
    {
        _activities[activity.Id] = Clone(activity);
        return Task.CompletedTask;
    }

    public Task SaveActivitiesAsync(IEnumerable<LocalActivity> activities, CancellationToken cancellationToken = default)
    {
        foreach (var activity in activities)
        {
            _activities[activity.Id] = Clone(activity);
        }

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLastSyncTimestampAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_lastSyncTimestamp);

    public Task SetLastSyncTimestampAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        _lastSyncTimestamp = timestamp;
        return Task.CompletedTask;
    }

    public LocalActivity? RawGet(Guid id) => _activities.TryGetValue(id, out var activity) ? activity : null;

    private static LocalActivity Clone(LocalActivity source)
    {
        return new LocalActivity
        {
            Id = source.Id,
            City = source.City,
            Name = source.Name,
            Description = source.Description,
            Cost = source.Cost,
            Activitytime = source.Activitytime,
            Latitude = source.Latitude,
            Longitude = source.Longitude,
            MinAge = source.MinAge,
            MaxAge = source.MaxAge,
            Category = source.Category,
            ImageUrl = source.ImageUrl,
            PlaceId = source.PlaceId,
            Rating = source.Rating,
            UpdatedAt = source.UpdatedAt,
            IsDeleted = source.IsDeleted,
            SyncState = source.SyncState
        };
    }
}

internal sealed class FakeNetworkStatus : INetworkStatus
{
    private bool _hasInternet;

    public FakeNetworkStatus(bool hasInternet = true)
    {
        _hasInternet = hasInternet;
    }

    public bool HasInternet => _hasInternet;

    public event Action<bool>? ConnectivityChanged;

    public void SetInternet(bool hasInternet)
    {
        _hasInternet = hasInternet;
        ConnectivityChanged?.Invoke(hasInternet);
    }
}

internal sealed class FakeSyncClient : IActivitySyncClient
{
    public List<ActivitySyncRecord> PushRequests { get; } = [];
    public PushChangesResult PushResult { get; set; } = new();
    public List<ActivitySyncRecord> PullItems { get; } = [];

    public Task<PushChangesResult> PushChangesAsync(IReadOnlyCollection<ActivitySyncRecord> items, CancellationToken cancellationToken = default)
    {
        PushRequests.AddRange(items);
        return Task.FromResult(PushResult);
    }

    public async IAsyncEnumerable<ActivitySyncRecord> PullChangesAsync(DateTimeOffset since, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in PullItems)
        {
            yield return item;
            await Task.Yield();
        }
    }
}

internal sealed class SequenceLocationProvider : ILocationProvider
{
    private readonly Queue<Func<Task<(double Latitude, double Longitude)>>> _responses = new();

    public void Enqueue(double latitude, double longitude)
    {
        _responses.Enqueue(() => Task.FromResult((latitude, longitude)));
    }

    public void EnqueueError(string message)
    {
        _responses.Enqueue(() => Task.FromException<(double Latitude, double Longitude)>(new InvalidOperationException(message)));
    }

    public Task<(double Latitude, double Longitude)> GetLocationAsync()
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No queued location response.");
        }

        return _responses.Dequeue()();
    }
}

internal sealed class StubTokenProvider : IAccessTokenProvider
{
    private readonly string? _token;

    public StubTokenProvider(string? token)
    {
        _token = token;
    }

    public Task<string?> GetTokenAsync() => Task.FromResult(_token);
}

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public List<HttpRequestMessage> Requests { get; } = [];

    public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_handler(request));
    }

    public static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
