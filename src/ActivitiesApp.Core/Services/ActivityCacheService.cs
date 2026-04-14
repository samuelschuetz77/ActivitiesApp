using ActivitiesApp.Data;
using ActivitiesApp.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ActivitiesApp.Services;

public class ActivityCacheService
{
    private readonly ILocalActivityStore _store;
    private readonly ILogger<ActivityCacheService> _logger;
    private readonly ConcurrentDictionary<Guid, Activity> _cache = new();
    private bool _isLoaded;

    public event Action? DataChanged;

    public ActivityCacheService(ILocalActivityStore store, ILogger<ActivityCacheService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task LoadFromDbAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var locals = await _store.ListActivitiesAsync(cancellationToken);

        _cache.Clear();
        foreach (var local in locals.Where(a => !a.IsDeleted))
        {
            _cache[local.Id] = ActivityMapping.ToActivity(local);
        }

        _isLoaded = true;
        sw.Stop();
        _logger.LogInformation("Cache loaded: {Count} activities in {Ms}ms", _cache.Count, sw.ElapsedMilliseconds);
        DataChanged?.Invoke();
    }

    public List<Activity> GetAll() => _cache.Values.ToList();

    public Activity? Get(Guid id)
    {
        _cache.TryGetValue(id, out var activity);
        return activity;
    }

    public void AddOrUpdate(Activity activity, bool suppressNotify = false)
    {
        _cache[activity.Id] = activity;
        if (!suppressNotify)
        {
            DataChanged?.Invoke();
        }
    }

    public void NotifyDataChanged()
    {
        DataChanged?.Invoke();
    }

    public bool IsLoaded => _isLoaded;
}
