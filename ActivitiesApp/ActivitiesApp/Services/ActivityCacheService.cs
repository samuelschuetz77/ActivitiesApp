using System.Collections.Concurrent;
using ActivitiesApp.Data;
using ActivitiesApp.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Services;

public class ActivityCacheService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActivityCacheService> _logger;
    private readonly ConcurrentDictionary<Guid, Activity> _cache = new();
    private bool _isLoaded;

    public event Action? DataChanged;

    public ActivityCacheService(IServiceScopeFactory scopeFactory, ILogger<ActivityCacheService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task LoadFromDbAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var locals = await db.Activities
            .Where(a => !a.IsDeleted)
            .ToListAsync();

        _cache.Clear();
        foreach (var local in locals)
        {
            _cache[local.Id] = ToActivity(local);
        }

        _isLoaded = true;
        sw.Stop();
        _logger.LogInformation("Cache loaded: {Count} activities in {Ms}ms", _cache.Count, sw.ElapsedMilliseconds);

        DataChanged?.Invoke();
    }

    public List<Activity> GetAll()
    {
        return _cache.Values.ToList();
    }

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

    private static Activity ToActivity(LocalActivity local)
    {
        return new Activity
        {
            Id = local.Id,
            Name = local.Name,
            City = local.City,
            Description = local.Description,
            Cost = local.Cost,
            Activitytime = local.Activitytime,
            Latitude = local.Latitude,
            Longitude = local.Longitude,
            MinAge = local.MinAge,
            MaxAge = local.MaxAge,
            Category = local.Category,
            ImageUrl = local.ImageUrl,
            PlaceId = local.PlaceId,
            Rating = local.Rating,
            UpdatedAt = local.UpdatedAt,
            IsDeleted = local.IsDeleted
        };
    }
}
