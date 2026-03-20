using ActivitiesApp.Data;
using ActivitiesApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ActivitiesApp;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Ensure SQLite database is created
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Warm the in-memory cache from SQLite before any page renders
            var cache = _serviceProvider.GetRequiredService<ActivityCacheService>();
            await cache.LoadFromDbAsync();

            // Start background auto-sync
            var syncService = _serviceProvider.GetRequiredService<SyncService>();
            syncService.StartAutoSync();
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogError(ex, "Failed to initialize offline sync");
            System.Diagnostics.Debug.WriteLine($"Sync init failed: {ex}");
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "ActivitiesApp" };
    }
}
